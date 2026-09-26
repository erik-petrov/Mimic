# Checks launch-at-startup on Windows, the way Windows does it at login. Run after building:
#   powershell -ExecutionPolicy Bypass -File conduit\Tests\check-autostart.ps1 conduit\bin\Release\Conduit.exe
# Uses a copy in a folder with a space in its path, marked as downloaded from the internet.
param([Parameter(Mandatory = $true)][string]$Exe)
$ErrorActionPreference = "Stop"
$name = "Mimic Conduit"
$run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$approved = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"

function Fail([string]$message) { Write-Host "FAIL $message" -ForegroundColor Red; exit 1 }
function Pass([string]$message) { Write-Host "PASS $message" -ForegroundColor Green }

$folder = Join-Path $env:TEMP "Mimic check with space"
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$copy = Join-Path $folder "Conduit.exe"
Copy-Item $Exe $copy -Force
Set-Content -Path $copy -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"

# Start from nothing, then switch it off the way Task Manager does (first byte odd).
Remove-ItemProperty -Path $run -Name $name -ErrorAction SilentlyContinue
New-Item -Path $approved -Force | Out-Null
New-ItemProperty -Path $approved -Name $name -PropertyType Binary -Value ([byte[]](3,0,0,0,0,0,0,0,0,0,0,0)) -Force | Out-Null

# Conduit's own code, as the Settings checkbox calls it.
$assembly = [Reflection.Assembly]::LoadFrom($copy)
$persistence = $assembly.GetType("Conduit.Persistence")
$launches = { $persistence.GetMethod("LaunchesAtStartup").Invoke($null, @()) }
$toggle = { $persistence.GetMethod("ToggleLaunchAtStartup").Invoke($null, @()) | Out-Null }

if (& $launches) { Fail "reports launching at startup before it was switched on" }
& $toggle
if (-not (& $launches)) { Fail "doesn't report launching at startup after switching it on" }
Pass "switching it on in Conduit works, also after it was switched off in Task Manager"

$command = (Get-ItemProperty -Path $run -Name $name).$name
$expected = "`"$copy`" --autostart"
if ($command -ne $expected) { Fail "startup command is <$command>, expected <$expected>" }
Pass "startup command is quoted: $command"

if ((Get-ItemProperty -Path $approved -Name $name -ErrorAction SilentlyContinue) -ne $null) { Fail "Task Manager still has it switched off" }
Pass "Task Manager's switch-off is undone"

if ((Get-Item -Path $copy -Stream * | Where-Object { $_.Stream -eq "Zone.Identifier" }) -ne $null) { Fail "the file is still marked as downloaded" }
Pass "the downloaded mark is removed"

# Start the command exactly as stored, like Windows does at login.
Get-Process Conduit -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Process -FilePath "cmd.exe" -ArgumentList "/c start `"`" $command" -WindowStyle Hidden
$process = $null
for ($i = 0; $i -lt 20 -and -not $process; $i++) {
    Start-Sleep -Milliseconds 500
    $process = Get-CimInstance Win32_Process -Filter "Name = 'Conduit.exe'" | Where-Object { $_.CommandLine -like "*--autostart*" }
}
if (-not $process) { Fail "Conduit didn't start from the startup command" }
Start-Sleep -Seconds 3
if (-not (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue)) { Fail "Conduit started but quit right away" }
Pass "Conduit starts from the startup command and keeps running: $($process.CommandLine)"
Stop-Process -Id $process.ProcessId -Force

& $toggle
if ((Get-ItemProperty -Path $run -Name $name -ErrorAction SilentlyContinue) -ne $null) { Fail "switching it off left the startup command" }
Pass "switching it off removes the startup command"
Write-Host "ALL PASSED"
