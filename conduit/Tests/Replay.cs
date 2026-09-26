// Replays a champ select recorded with tools/lcu-dump.ps1 -Record through autopick, and prints
// what autopick did and sent. Checks autopick against a real client's data.
//
//   mcs -out:replay.exe -r:Microsoft.CSharp -r:System.Runtime.Serialization Autopick.cs SimpleJson.cs Tests/Replay.cs
//   mono replay.exe <dump>/events.ndjson <dump>/snapshots/00 '{"any":{"picks":[{"championId":64}],"bans":[157,238]}}'
//
// The snapshot folder answers the requests autopick makes (pickable and bannable champions, names).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Conduit.Tests
{
    class ReplayApi : ILeagueApi
    {
        public string Snap;
        public List<string> Writes = new List<string>();
        public Task<ApiResult> Request(string method, string path, string body)
        {
            if (method != "GET") { lock (Writes) Writes.Add(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + method + " " + path + " " + body); return Task.FromResult(new ApiResult { Status = 204 }); }
            string file = null;
            if (path == "/lol-champ-select/v1/pickable-champion-ids") file = "lol-champ-select_v1_pickable-champion-ids.json";
            if (path == "/lol-champ-select/v1/bannable-champion-ids") file = "lol-champ-select_v1_bannable-champion-ids.json";
            if (path == "/lol-gameflow/v1/session") file = "lol-gameflow_v1_session.json";
            if (path == "/lol-game-data/assets/v1/champion-summary.json") file = "lol-champ-select_v1_all-grid-champions.json";
            if (file == null) return Task.FromResult(new ApiResult { Status = 404 });
            var text = File.ReadAllText(Path.Combine(Snap, file)).TrimStart('﻿');
            return Task.FromResult(new ApiResult { Status = 200, Content = SimpleJson.DeserializeObject(text) });
        }
    }

    class Replay
    {
        static void Main(string[] args)
        {
            var api = new ReplayApi { Snap = args[1] };
            var engine = new Autopick(api, null, null, () => Task.FromResult((int?) 0), msg => Console.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " log: " + msg));
            engine.SetRoles(SimpleJson.DeserializeObject(args[2]));
            engine.SetEnabled(true);

            DateTime? last = null;
            foreach (var line in File.ReadLines(args[0]))
            {
                var ev = SimpleJson.DeserializeObject(line.TrimStart('﻿')) as JsonObject;
                var msg = ev != null && ev.ContainsKey("msg") ? ev["msg"] as JsonArray : null;
                if (msg == null || (long) msg[0] != 8) continue;
                var body = (JsonObject) msg[2];
                var uri = (string) body["uri"];
                if (uri != "/lol-champ-select/v1/session" && uri != "/lol-gameflow/v1/gameflow-phase") continue;

                var time = DateTime.Parse((string) ev["time"]).ToUniversalTime();
                if (last != null) Thread.Sleep((int) Math.Min(1500, Math.Max(30, (time - last.Value).TotalMilliseconds / 10)));
                last = time;

                var data = body.ContainsKey("data") ? body["data"] : null;
                var s = data as JsonObject;
                if (s != null)
                {
                    var me = (long) s["localPlayerCellId"];
                    var mine = ((JsonArray) s["actions"]).Cast<JsonArray>().SelectMany(x => x.Cast<JsonObject>()).Where(a => (long) a["actorCellId"] == me)
                        .Select(a => a["type"] + " " + a["id"] + " c=" + a["championId"] + (true.Equals(a["completed"]) ? " done" : "") + (true.Equals(a["isInProgress"]) ? " NOW" : ""));
                    Console.WriteLine(time.ToString("HH:mm:ss.fff") + " event: " + ((JsonObject) s["timer"])["phase"] + " cell " + me + " | " + string.Join(", ", mine));
                }
                else Console.WriteLine(time.ToString("HH:mm:ss.fff") + " event: " + uri + " " + body["eventType"] + " " + data);
                engine.HandleEvent(uri, (string) body["eventType"], data);
            }
            Thread.Sleep(1500);
            Console.WriteLine("--- sent to the client:");
            lock (api.Writes) foreach (var w in api.Writes) Console.WriteLine(w);
            Console.WriteLine("--- final status: " + engine.Status);
        }
    }
}
