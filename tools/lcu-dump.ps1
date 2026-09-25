<#
.SYNOPSIS
    Dumps the API of your running League client (the LCU), and optionally records
    every event it sends while you click around in the real client.

.DESCRIPTION
    Finds the running League client, reads its local port and password the same way
    Conduit does, and writes everything into a new folder:

      meta.json               client build info and which requests worked
      help.json               GET /help, the list of functions, events and types
      help-full.json          GET /help?format=Full, the same with arguments and return types
      openapi-v3.json         only if the client still serves its swagger spec
      swagger-v2.json         only if the client still serves its swagger spec
      snapshots\...           current state of champ select, runes, lobby (with -Record)
      events.ndjson           every event the client sent, one per line (with -Record)

    The client password is never written to disk.

.PARAMETER Record
    After the dump, keep listening to the client's event socket and log every event
    until you press Q or Ctrl+C. While recording:
      M  writes a numbered marker, so you can note "marker 3 = requested lane swap"
      S  saves a snapshot of the champ select, rune and lobby endpoints
      Q  stops recording

.PARAMETER TestAutoRunes
    Instead of dumping, tests auto runes during champ select: asks the client for
    auto runes the way Mimic does, watches what the client does for a few seconds,
    and fetches the recommended pages directly. Pick or hover a champion first.
    Writes autorunes-test.json and recommended-pages.json.

.PARAMETER OutDir
    Where to write the dump. Defaults to .\lcu-dump-<date>-<time>.

.PARAMETER LockfilePath
    Path to League's lockfile, if the client can't be found automatically.
    Usually C:\Riot Games\League of Legends\lockfile.

.PARAMETER ExcludePrefix
    Event paths that are not recorded. Defaults to chat and messaging, so your
    private messages don't end up in the file.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\lcu-dump.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\lcu-dump.ps1 -Record

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\lcu-dump.ps1 -TestAutoRunes
#>
[CmdletBinding()]
param(
    [switch]$Record,
    [switch]$TestAutoRunes,
    [string]$OutDir,
    [string]$LockfilePath,
    [string[]]$ExcludePrefix = @("/lol-chat/", "/riot-messaging-service/", "/lol-hovercard/", "/lol-game-client-chat/")
)

$ErrorActionPreference = "Stop"

# Written in C# 5 so that it compiles on both Windows PowerShell 5.1 and PowerShell 7.
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition @"
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;

public class LcuResponse
{
    public int Status;
    public string Body;
}

public static class LcuHelper
{
    // The League client uses a certificate signed by Riot's own authority, which Windows
    // does not trust. The check is skipped for our own requests to the local client only.
    public static bool AcceptAll(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
    {
        return true;
    }

    // Used for the process-wide callback on Windows PowerShell 5.1, where the websocket
    // has no per-socket option. Anything that is not a request to this machine still
    // gets the normal check.
    public static bool LoopbackOnly(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;
        HttpWebRequest req = sender as HttpWebRequest;
        return req != null && req.RequestUri.IsLoopback;
    }

    // Windows PowerShell 5.1 can't turn a method into a callback, so it's installed from here.
    public static void UseLoopbackOnlyCallback()
    {
        ServicePointManager.ServerCertificateValidationCallback = LoopbackOnly;
    }

    public static bool SocketHasOwnCallback
    {
        get { return typeof(ClientWebSocketOptions).GetProperty("RemoteCertificateValidationCallback") != null; }
    }

    public static LcuResponse Request(string method, string url, string auth)
    {
        HttpWebRequest req = (HttpWebRequest) WebRequest.Create(url);
        req.Method = method;
        req.Accept = "application/json";
        req.Headers[HttpRequestHeader.Authorization] = "Basic " + auth;
        req.ServerCertificateValidationCallback = AcceptAll;
        req.Timeout = 120000;
        if (method != "GET") req.ContentLength = 0;
        req.ReadWriteTimeout = 120000;

        HttpWebResponse res;
        try
        {
            res = (HttpWebResponse) req.GetResponse();
        }
        catch (WebException e)
        {
            res = e.Response as HttpWebResponse;
            if (res == null) throw;
        }

        using (res)
        using (StreamReader reader = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
        {
            LcuResponse result = new LcuResponse();
            result.Status = (int) res.StatusCode;
            result.Body = reader.ReadToEnd();
            return result;
        }
    }

    public static ClientWebSocket CreateSocket(string auth)
    {
        ClientWebSocket ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", "Basic " + auth);
        ws.Options.AddSubProtocol("wamp");

        PropertyInfo prop = typeof(ClientWebSocketOptions).GetProperty("RemoteCertificateValidationCallback");
        if (prop != null) prop.SetValue(ws.Options, new RemoteCertificateValidationCallback(AcceptAll), null);

        return ws;
    }
}
"@

# Snapshot endpoints. These hold the current state of the screens Mimic cares about.
$SnapshotPaths = @(
    "/lol-gameflow/v1/session",
    "/lol-champ-select/v1/session",
    "/lol-champ-select/v1/all-grid-champions",
    "/lol-champ-select/v1/pickable-champion-ids",
    "/lol-champ-select/v1/bannable-champion-ids",
    "/lol-champ-select/v1/skin-carousel-skins",
    "/lol-perks/v1/pages",
    "/lol-perks/v1/currentpage",
    "/lol-perks/v1/inventory",
    "/lol-perks/v1/styles",
    "/lol-lobby/v2/lobby"
)

function Read-Lockfile([string]$path) {
    # League keeps the lockfile open, so it has to be opened with shared access.
    $stream = [System.IO.File]::Open($path, "Open", "Read", "ReadWrite")
    try {
        $content = (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally {
        $stream.Dispose()
    }

    # Format: name:pid:port:password:protocol
    $parts = $content.Trim().Split(":")
    if ($parts.Length -lt 5) { throw "Unexpected lockfile format in $path" }
    return @{ Port = [int]$parts[2]; Token = $parts[3] }
}

function Get-LcuCredentials {
    if ($LockfilePath) { return Read-Lockfile $LockfilePath }

    $proc = $null
    try {
        $proc = Get-CimInstance Win32_Process -Filter "Name = 'LeagueClientUx.exe'" | Select-Object -First 1
    } catch {
        # Not on Windows, or CIM is unavailable. Fall through to the lockfile.
    }

    # Same approach as Conduit's LeagueUtils.cs: read the arguments the client was started with.
    if ($proc -and $proc.CommandLine) {
        $portMatch = [regex]::Match($proc.CommandLine, "--app-port=(\d+)")
        $tokenMatch = [regex]::Match($proc.CommandLine, "--remoting-auth-token=([^`"\s]+)")
        if ($portMatch.Success -and $tokenMatch.Success) {
            return @{ Port = [int]$portMatch.Groups[1].Value; Token = $tokenMatch.Groups[1].Value }
        }
    }

    $candidates = @()
    if ($proc -and $proc.ExecutablePath) { $candidates += Join-Path (Split-Path $proc.ExecutablePath) "lockfile" }
    $candidates += "C:\Riot Games\League of Legends\lockfile"

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return Read-Lockfile $candidate }
    }

    throw "Could not find a running League client. Start League and log in, or pass -LockfilePath."
}

function Invoke-Lcu([string]$path, [string]$method = "GET") {
    return [LcuHelper]::Request($method, "https://127.0.0.1:$($script:Creds.Port)$path", $script:Auth)
}

function Save-Endpoint([string]$path, [string]$file) {
    $label = "{0,-45}" -f $path
    try {
        $res = Invoke-Lcu $path
    } catch {
        Write-Host "  $label failed: $($_.Exception.Message)" -ForegroundColor Red
        return @{ path = $path; status = 0; error = $_.Exception.Message }
    }

    $size = [System.Text.Encoding]::UTF8.GetByteCount($res.Body)
    if ($res.Status -eq 200) {
        [System.IO.File]::WriteAllText($file, $res.Body, (New-Object System.Text.UTF8Encoding($false)))
        $sizeText = if ($size -lt 1KB) { "$size bytes" } else { "{0:N0} KB" -f ($size / 1KB) }
        Write-Host "  $label $($res.Status) ($sizeText)" -ForegroundColor Green
    } else {
        Write-Host "  $label $($res.Status)" -ForegroundColor DarkGray
    }
    return @{ path = $path; status = $res.Status; bytes = $size }
}

function Save-Snapshot([string]$dir) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    foreach ($path in $SnapshotPaths) {
        $name = ($path.TrimStart("/") -replace "[/\\?:*]", "_") + ".json"
        Save-Endpoint $path (Join-Path $dir $name) | Out-Null
    }
}

# Pulls the event path out of a raw [8, "OnJsonApiEvent", {...}] message without parsing
# the whole payload, which can be several megabytes. The client writes "uri" as the last
# key; if that ever changes, the full parse below is the fallback.
function Get-EventUri([string]$raw) {
    $m = [regex]::Match($raw, '"uri"\s*:\s*"((?:[^"\\]|\\.)*)"\s*\}\s*\]\s*$')
    if ($m.Success) { return $m.Groups[1].Value }
    try { return ($raw | ConvertFrom-Json)[2].uri } catch { return $null }
}

function Start-Recording([string]$dir) {
    $eventsFile = Join-Path $dir "events.ndjson"
    $writer = New-Object System.IO.StreamWriter($eventsFile, $false, (New-Object System.Text.UTF8Encoding($false)))
    $writer.AutoFlush = $true

    $previousCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
    if (-not [LcuHelper]::SocketHasOwnCallback) {
        [LcuHelper]::UseLoopbackOnlyCallback()
    }

    $ws = [LcuHelper]::CreateSocket($script:Auth)
    $recorded = 0
    $skipped = 0
    $markers = 0
    $snapshots = 0

    try {
        try {
            $ws.ConnectAsync([Uri]"wss://127.0.0.1:$($script:Creds.Port)/", [Threading.CancellationToken]::None).Wait()
        } catch {
            $hint = if ($PSVersionTable.PSEdition -ne "Core") { " If this keeps happening, install PowerShell 7 and run the script with pwsh instead." } else { "" }
            throw "Could not open the client's event socket: $($_.Exception.InnerException.Message)$hint"
        }

        # WAMP subscribe to every JSON API event, same as Conduit's LeagueConnection.cs.
        $subscribe = [System.Text.Encoding]::UTF8.GetBytes('[5,"OnJsonApiEvent"]')
        $ws.SendAsync((New-Object System.ArraySegment[byte] -ArgumentList (, $subscribe)), "Text", $true, [Threading.CancellationToken]::None).Wait()

        $interactive = $true
        try { [void][Console]::KeyAvailable } catch { $interactive = $false }

        Write-Host ""
        Write-Host "Recording to $eventsFile" -ForegroundColor Cyan
        if ($interactive) {
            Write-Host "Keys: M = marker, S = snapshot, Q = stop. Ctrl+C also stops." -ForegroundColor Cyan
        } else {
            Write-Host "Press Ctrl+C to stop." -ForegroundColor Cyan
        }

        $buffer = New-Object byte[] 65536
        $segment = New-Object System.ArraySegment[byte] -ArgumentList (, $buffer)
        $message = New-Object System.IO.MemoryStream
        $receive = $ws.ReceiveAsync($segment, [Threading.CancellationToken]::None)

        while ($true) {
            if (-not $receive.Wait(200)) {
                if (-not $interactive -or -not [Console]::KeyAvailable) { continue }

                $key = [Console]::ReadKey($true).Key
                $time = [DateTime]::UtcNow.ToString("o")
                if ($key -eq "Q") { break }
                if ($key -eq "M") {
                    $markers++
                    $writer.WriteLine("{`"time`":`"$time`",`"marker`":$markers}")
                    Write-Host "  marker $markers written. Note down what you did." -ForegroundColor Yellow
                }
                if ($key -eq "S") {
                    $snapshots++
                    $snapDir = Join-Path $dir ("snapshots\{0:D2}" -f $snapshots)
                    $writer.WriteLine("{`"time`":`"$time`",`"snapshot`":$snapshots}")
                    Write-Host "  snapshot $($snapshots):" -ForegroundColor Yellow
                    Save-Snapshot $snapDir
                }
                continue
            }

            $result = $receive.Result
            if ($result.MessageType -eq "Close") {
                Write-Host "The client closed the connection." -ForegroundColor Yellow
                break
            }

            $message.Write($buffer, 0, $result.Count)
            $receive = $ws.ReceiveAsync($segment, [Threading.CancellationToken]::None)
            if (-not $result.EndOfMessage) { continue }

            $raw = [System.Text.Encoding]::UTF8.GetString($message.ToArray())
            $message.SetLength(0)
            if ([string]::IsNullOrWhiteSpace($raw)) { continue }

            $uri = Get-EventUri $raw
            $excluded = $false
            if ($uri) {
                foreach ($prefix in $ExcludePrefix) {
                    if ($uri.StartsWith($prefix)) { $excluded = $true; break }
                }
            }
            if ($excluded) { $skipped++; continue }

            $time = [DateTime]::UtcNow.ToString("o")
            $writer.WriteLine("{`"time`":`"$time`",`"msg`":$raw}")
            $recorded++
            if ($uri) { Write-Host "  $uri" -ForegroundColor DarkGray }
        }
    } finally {
        if ($ws.State -eq "Open") {
            try { $ws.CloseAsync("NormalClosure", "", [Threading.CancellationToken]::None).Wait(2000) | Out-Null } catch { }
        }
        $ws.Dispose()
        $writer.Dispose()
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $previousCallback

        Write-Host ""
        Write-Host "Recorded $recorded events ($skipped chat/messaging events left out), $markers markers, $snapshots snapshots." -ForegroundColor Cyan
    }
}

function Get-LcuJson([string]$path, [string]$method = "GET") {
    $res = Invoke-Lcu $path $method
    $parsed = $null
    if ($res.Body) {
        try { $parsed = $res.Body | ConvertFrom-Json } catch { }
    }
    return @{ status = $res.Status; data = $parsed; body = $res.Body }
}

function Get-RuneState {
    $current = (Get-LcuJson "/lol-perks/v1/currentpage").data
    $pages = @((Get-LcuJson "/lol-perks/v1/pages").data)
    $flag = (Get-LcuJson "/lol-perks/v1/rune-recommender-auto-select").data
    return [ordered]@{
        autoSelectFlag = $flag
        currentPage = if ($current) { [ordered]@{ id = $current.id; name = $current.name; isTemporary = $current.isTemporary; recommendationChampionId = $current.recommendationChampionId } } else { $null }
        pageCount = $pages.Count
        temporaryPages = @($pages | Where-Object { $_.isTemporary } | ForEach-Object { [ordered]@{ id = $_.id; name = $_.name; recommendationChampionId = $_.recommendationChampionId } })
    }
}

function Test-AutoRunes([string]$dir) {
    $session = (Get-LcuJson "/lol-champ-select/v1/session").data
    if (-not $session) { throw "Not in champ select. Start a game (a practice tool lobby works), pick or hover a champion, and run this again." }

    $me = @($session.myTeam | Where-Object { $_.cellId -eq $session.localPlayerCellId })[0]
    $champion = if ($me.championId) { $me.championId } else { $me.championPickIntent }
    if (-not $champion) { throw "Pick or hover a champion first, then run this again." }

    $gameflow = (Get-LcuJson "/lol-gameflow/v1/session").data
    $mapId = if ($gameflow -and $gameflow.map) { $gameflow.map.id } else { 11 }

    $report = [ordered]@{
        testedAt = [DateTime]::UtcNow.ToString("o")
        championId = $champion
        assignedPosition = $me.assignedPosition
        mapId = $mapId
        before = Get-RuneState
    }

    Write-Host "Champion $champion, position '$($me.assignedPosition)', map $mapId."
    Write-Host "Asking the client for auto runes, like Mimic's wand button..."
    $post = Invoke-Lcu "/lol-perks/v1/rune-recommender-auto-select" "POST"
    $report.post = [ordered]@{ status = $post.Status; body = $post.Body }
    Write-Host "  POST /lol-perks/v1/rune-recommender-auto-select -> $($post.Status) $($post.Body)"

    # Watch what the client does over the next few seconds.
    $timeline = @()
    $last = ""
    $start = Get-Date
    for ($i = 0; $i -lt 16; $i++) {
        Start-Sleep -Milliseconds 500
        $state = Get-RuneState
        $text = $state | ConvertTo-Json -Depth 5 -Compress
        if ($text -ne $last) {
            $seconds = [Math]::Round(((Get-Date) - $start).TotalSeconds, 1)
            $timeline += [ordered]@{ seconds = $seconds; state = $state }
            $page = if ($state.currentPage) { "$($state.currentPage.name) (temporary: $($state.currentPage.isTemporary))" } else { "none" }
            Write-Host "  after $($seconds)s: flag $($state.autoSelectFlag), current page $page"
            $last = $text
        }
    }
    $report.timeline = $timeline

    # Ask for the recommended pages directly, with the position as the session writes it and in capitals.
    $position = $me.assignedPosition
    if (-not $position) {
        $position = (Get-LcuJson "/lol-perks/v1/recommended-pages/position/champion/$champion").data
        $report.defaultPosition = $position
    }

    $report.recommended = @()
    $saved = $false
    foreach ($candidate in (@("$position", "$position".ToUpper()) | Select-Object -Unique)) {
        $path = "/lol-perks/v1/recommended-pages/champion/$champion/position/$candidate/map/$mapId"
        $rec = Get-LcuJson $path
        $pages = @($rec.data | Where-Object { $_ -and $_.keystone })
        $count = if ($rec.status -eq 200) { $pages.Count } else { 0 }
        $report.recommended += [ordered]@{
            path = $path
            status = $rec.status
            count = $count
            pages = @($pages | ForEach-Object {
                [ordered]@{
                    position = $_.position; keystone = $_.keystone.name; primaryPerkStyleId = $_.primaryPerkStyleId
                    secondaryPerkStyleId = $_.secondaryPerkStyleId; perkIds = @($_.perks | ForEach-Object { $_.id })
                    summonerSpellIds = $_.summonerSpellIds; recommendationId = $_.recommendationId
                }
            })
        }
        Write-Host "  GET $path -> $($rec.status), $count pages"
        if ($rec.status -eq 200 -and -not $saved) {
            [System.IO.File]::WriteAllText((Join-Path $dir "recommended-pages.json"), $rec.body, (New-Object System.Text.UTF8Encoding($false)))
            $saved = $true
        }
    }

    [System.IO.File]::WriteAllText((Join-Path $dir "autorunes-test.json"), ($report | ConvertTo-Json -Depth 8), (New-Object System.Text.UTF8Encoding($false)))
}

if ($PSVersionTable.PSEdition -ne "Core") {
    # Windows PowerShell 5.1 may not enable TLS 1.2 by default.
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor [System.Net.SecurityProtocolType]::Tls12
}

$script:Creds = Get-LcuCredentials
$script:Auth = [Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes("riot:" + $script:Creds.Token))

try {
    $check = Invoke-Lcu "/system/v1/builds"
} catch {
    throw "Could not reach the League client on port $($script:Creds.Port): $($_.Exception.Message)"
}
if ($check.Status -eq 401) { throw "The League client rejected the password. If League restarted, run the script again." }

if (-not $OutDir) { $OutDir = Join-Path (Get-Location) ("lcu-dump-" + (Get-Date -Format "yyyyMMdd-HHmmss")) }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

Write-Host "Found the League client on port $($script:Creds.Port). Writing to $OutDir"

if ($TestAutoRunes) {
    Write-Host ""
    Test-AutoRunes $OutDir
    Write-Host ""
    Write-Host "Done. Send autorunes-test.json from $OutDir" -ForegroundColor Cyan
    return
}
Write-Host ""
Write-Host "API reference:"

$results = @(
    (Save-Endpoint "/system/v1/builds" (Join-Path $OutDir "builds.json")),
    (Save-Endpoint "/help" (Join-Path $OutDir "help.json")),
    (Save-Endpoint "/help?format=Full" (Join-Path $OutDir "help-full.json")),
    (Save-Endpoint "/swagger/v3/openapi.json" (Join-Path $OutDir "openapi-v3.json")),
    (Save-Endpoint "/swagger/v2/swagger.json" (Join-Path $OutDir "swagger-v2.json"))
)

$meta = [ordered]@{
    dumpedAt = [DateTime]::UtcNow.ToString("o")
    powershell = $PSVersionTable.PSVersion.ToString()
    requests = $results
}
[System.IO.File]::WriteAllText((Join-Path $OutDir "meta.json"), ($meta | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))

if ($Record) {
    Write-Host ""
    Write-Host "Starting snapshot:"
    Save-Snapshot (Join-Path $OutDir "snapshots\00")
    Start-Recording $OutDir
}

Write-Host ""
Write-Host "Done. Files are in $OutDir" -ForegroundColor Cyan
if ($Record) {
    Write-Host "events.ndjson and snapshots contain player names and ids from your games. Look them over before sharing." -ForegroundColor Yellow
}
