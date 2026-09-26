using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Conduit.Tests
{
    /**
     * A League client that keeps a champ select and rune pages the way the real one does. Changes
     * to the champ select reach autopick as events a little later, like the real client's.
     */
    class FakeClient : ILeagueApi
    {
        public Autopick Engine;
        public JsonObject Session;
        public readonly List<string> Calls = new List<string>();

        public List<JsonObject> Pages = new List<JsonObject>();
        public long CurrentPage;
        public bool RefuseTemporaryPages;
        public bool DropRunes;

        // Champions this champ select offers. Normal champions unless a test changes it.
        public long[] Offered = NAMES.Keys.ToArray();

        // What the client says can be banned. Null answers [-1], like the real client did.
        public JsonArray Bannable;

        // The game mode of this champ select, and whether the client ignores spell changes.
        public string GameMode = "CLASSIC";
        public bool IgnoreSpells;
        private long nextPageId = 1000;

        public static readonly Dictionary<long, string> NAMES = new Dictionary<long, string>
        {
            { 64, "Lee Sin" }, { 91, "Talon" }, { 32, "Amumu" }, { 29, "Twitch" }, { 157, "Yasuo" }, { 238, "Zed" }, { 103, "Ahri" }, { 875, "Sett" }
        };

        public FakeClient()
        {
            Pages.Add(Page(1, "TWITCH OTP", false));
            Pages.Add(Page(2, "TALON JG", false));
            CurrentPage = 1;
        }

        public static JsonObject Page(long id, string name, bool temporary)
        {
            var page = new JsonObject();
            page["id"] = id;
            page["name"] = name;
            page["isTemporary"] = temporary;
            page["isEditable"] = true;
            page["primaryStyleId"] = 8000L;
            page["subStyleId"] = 8100L;
            page["selectedPerkIds"] = Arr(8005, 9111, 9104, 8014, 8139, 8135, 5005, 5008, 5001);
            return page;
        }

        public static JsonArray Arr(params long[] values)
        {
            var array = new JsonArray();
            foreach (var value in values) array.Add(value);
            return array;
        }

        public List<string> Writes()
        {
            lock (Calls) return Calls.Where(x => !x.StartsWith("GET ")).ToList();
        }

        public Task<ApiResult> Request(string method, string path, string body)
        {
            lock (Calls) Calls.Add(method + " " + path + (body != null ? " " + body : ""));
            lock (this) return Task.FromResult(Handle(method, path, body));
        }

        private ApiResult Handle(string method, string path, string body)
        {
            if (method == "GET" && path == "/lol-champ-select/v1/pickable-champion-ids") return Ok(Arr(Offered));
            // The real client answered [-1] ("no ban") for a whole draft in which every champion could be banned.
            if (method == "GET" && path == "/lol-champ-select/v1/bannable-champion-ids") return Ok(Bannable ?? Arr(-1));
            if (method == "GET" && path == "/lol-game-data/assets/v1/champion-summary.json")
            {
                var list = new JsonArray();
                // League Classic champions have the same names, with 60000 added to the id.
                foreach (var entry in NAMES)
                {
                    foreach (var id in new[] { entry.Key, entry.Key + 60000 })
                    {
                        var champion = new JsonObject();
                        champion["id"] = id;
                        champion["name"] = entry.Value;
                        list.Add(champion);
                    }
                }
                return Ok(list);
            }
            if (method == "GET" && path == "/lol-gameflow/v1/session") return Ok(SimpleJson.DeserializeObject("{\"map\":{\"id\":11},\"gameData\":{\"queue\":{\"gameMode\":\"" + GameMode + "\"}}}"));
            if (method == "GET" && path == "/lol-champ-select/v1/session") return Ok(Copy(Session));
            if (method == "GET" && path == "/lol-game-data/assets/v1/summoner-spells.json")
            {
                return Ok(SimpleJson.DeserializeObject("[{\"id\":4,\"gameModes\":[\"CLASSIC\",\"ARAM\"]},{\"id\":7,\"gameModes\":[\"CLASSIC\",\"ARAM\"]},{\"id\":11,\"gameModes\":[\"CLASSIC\"]},{\"id\":12,\"gameModes\":[\"CLASSIC\"]},{\"id\":14,\"gameModes\":[\"CLASSIC\",\"ARAM\"]},{\"id\":32,\"gameModes\":[\"ARAM\"]}]"));
            }
            if (method == "GET" && path.StartsWith("/lol-perks/v1/recommended-pages/champion/"))
            {
                var champion = long.Parse(path.Split('/')[5]);
                return Ok(SimpleJson.DeserializeObject("[{\"recommendationId\":\"rec" + champion + "\",\"keystone\":{\"id\":8010,\"name\":\"Conqueror\"},\"perks\":[{\"id\":8010},{\"id\":9111},{\"id\":9104},{\"id\":8299},{\"id\":8143},{\"id\":8135},{\"id\":5005},{\"id\":5008},{\"id\":5001}],\"primaryPerkStyleId\":8000,\"secondaryPerkStyleId\":8100}]"));
            }
            if (method == "GET" && path == "/lol-perks/v1/pages")
            {
                var list = new JsonArray();
                foreach (var page in Pages) list.Add(Copy(page));
                return Ok(list);
            }
            if (method == "GET" && path == "/lol-perks/v1/currentpage") return Ok(Copy(Pages.First(x => (long) x["id"] == CurrentPage)));
            if (method == "PUT" && path == "/lol-perks/v1/currentpage")
            {
                var id = long.Parse(body);
                if (!Pages.Any(x => (long) x["id"] == id)) return Fail(404, "No page");
                CurrentPage = id;
                return new ApiResult { Status = 204 };
            }
            if (method == "POST" && path == "/lol-perks/v1/pages")
            {
                if (RefuseTemporaryPages) return Fail(400, "Max pages reached");
                var page = (JsonObject) SimpleJson.DeserializeObject(body);
                page["id"] = nextPageId++;
                page["isEditable"] = true;
                if (DropRunes) page["selectedPerkIds"] = Arr(0, 0, 0, 0, 0, 0, 0, 0, 0);
                Pages.Add(page);
                return Ok(Copy(page));
            }
            if (method == "PUT" && path.StartsWith("/lol-perks/v1/pages/"))
            {
                var id = long.Parse(path.Substring("/lol-perks/v1/pages/".Length));
                var page = Pages.First(x => (long) x["id"] == id);
                foreach (var entry in (JsonObject) SimpleJson.DeserializeObject(body)) page[entry.Key] = entry.Value;
                page["id"] = id;
                if (DropRunes) page["selectedPerkIds"] = Arr(0, 0, 0, 0, 0, 0, 0, 0, 0);
                return Ok(Copy(page));
            }
            if (method == "PATCH" && path == "/lol-champ-select/v1/session/my-selection")
            {
                var change = (JsonObject) SimpleJson.DeserializeObject(body);
                var me = Me();

                // The client's Smite rule: a jungler keeps Smite, nobody else gets it.
                if (change.ContainsKey("spell1Id"))
                {
                    var smite = (long) change["spell1Id"] == 11 || (long) change["spell2Id"] == 11;
                    var position = (string) me["assignedPosition"];
                    if (position == "jungle" && !smite) return Fail(400, "Junglers must take Smite");
                    if (position != "" && position != "jungle" && smite) return Fail(400, "Only junglers can take Smite");
                    if (IgnoreSpells) return new ApiResult { Status = 204 };
                }

                foreach (var entry in change) me[entry.Key] = entry.Value;
                PublishLater();
                return new ApiResult { Status = 204 };
            }
            if (method == "PATCH" && path.StartsWith("/lol-champ-select/v1/session/actions/"))
            {
                var id = long.Parse(path.Substring("/lol-champ-select/v1/session/actions/".Length));
                var change = (JsonObject) SimpleJson.DeserializeObject(body);
                var action = Action(id);
                if (action == null) return Fail(404, "No action");
                if (!(bool) action["isInProgress"] && change.ContainsKey("completed")) return Fail(500, "Not your turn");

                ApplyAction(action, (long) change["championId"], change.ContainsKey("completed") && (bool) change["completed"]);
                PublishLater();
                return new ApiResult { Status = 204 };
            }

            return Fail(404, "Unknown " + method + " " + path);
        }

        public void ApplyAction(JsonObject action, long championId, bool completed)
        {
            action["championId"] = championId;
            var member = Member((long) action["actorCellId"]);
            if (completed)
            {
                action["completed"] = true;
                action["isInProgress"] = false;
                if ((string) action["type"] == "pick" && member != null)
                {
                    member["championId"] = championId;
                    member["championPickIntent"] = 0L;
                }
            }
            else if ((string) action["type"] == "pick" && member != null)
            {
                member["championPickIntent"] = championId;
            }
        }

        public JsonObject Action(long id)
        {
            return ((JsonArray) Session["actions"]).Cast<JsonArray>().SelectMany(x => x.Cast<JsonObject>()).FirstOrDefault(x => (long) x["id"] == id);
        }

        public IEnumerable<JsonObject> AllActions()
        {
            return ((JsonArray) Session["actions"]).Cast<JsonArray>().SelectMany(x => x.Cast<JsonObject>());
        }

        public JsonObject Member(long cell)
        {
            return ((JsonArray) Session["myTeam"]).Concat((JsonArray) Session["theirTeam"]).Cast<JsonObject>().FirstOrDefault(x => (long) x["cellId"] == cell);
        }

        public JsonObject Me()
        {
            return Member((long) Session["localPlayerCellId"]);
        }

        /**
         * Changes the champ select as the client would, then sends the event.
         */
        public void Change(Action<FakeClient> change)
        {
            lock (this) change(this);
            Publish();
        }

        public void Publish()
        {
            JsonObject copy;
            lock (this) copy = Copy(Session);
            Engine.HandleEvent("/lol-champ-select/v1/session", "Update", copy);
        }

        private void PublishLater()
        {
            Task.Delay(30).ContinueWith(_ => Publish());
        }

        public static JsonObject Copy(JsonObject value)
        {
            return (JsonObject) SimpleJson.DeserializeObject(SimpleJson.SerializeObject(value));
        }

        private static ApiResult Ok(object content)
        {
            return new ApiResult { Status = 200, Content = content };
        }

        private static ApiResult Fail(int status, string message)
        {
            var content = new JsonObject();
            content["message"] = message;
            return new ApiResult { Status = status, Content = content };
        }

        /**
         * A draft champ select where we are cell 1 (jungle). Bans are actions 0 to 9, one per cell.
         * Picks go 0, then 5 6, then 1 2, then 7 8, then 3 4, then 9, as actions 10 to 19.
         * A blind pick has no bans and everyone picks at once.
         */
        public static JsonObject NewSession(string id, bool draft, string myPosition = "jungle")
        {
            var positions = new[] { "top", myPosition, "middle", "bottom", "utility" };
            var session = new JsonObject();
            session["id"] = id;
            session["gameId"] = 1L;
            session["localPlayerCellId"] = 1L;
            session["hasSimultaneousPicks"] = !draft;

            var myTeam = new JsonArray();
            var theirTeam = new JsonArray();
            for (var cell = 0L; cell < 10; cell++)
            {
                var member = new JsonObject();
                member["cellId"] = cell;
                member["championId"] = 0L;
                member["championPickIntent"] = 0L;
                member["assignedPosition"] = cell < 5 && draft ? positions[cell] : "";
                member["spell1Id"] = 4L;
                member["spell2Id"] = 7L;
                member["selectedSkinId"] = 0L;
                (cell < 5 ? myTeam : theirTeam).Add(member);
            }
            session["myTeam"] = myTeam;
            session["theirTeam"] = theirTeam;

            var actions = new JsonArray();
            if (draft)
            {
                var bans = new JsonArray();
                for (var cell = 0L; cell < 10; cell++) bans.Add(NewAction(cell, cell, "ban"));
                actions.Add(bans);

                var order = new[] { new long[] { 0 }, new long[] { 5, 6 }, new long[] { 1, 2 }, new long[] { 7, 8 }, new long[] { 3, 4 }, new long[] { 9 } };
                var next = 10L;
                foreach (var group in order)
                {
                    var picks = new JsonArray();
                    foreach (var cell in group) picks.Add(NewAction(next++, cell, "pick"));
                    actions.Add(picks);
                }
            }
            else
            {
                var picks = new JsonArray();
                for (var cell = 0L; cell < 10; cell++)
                {
                    var action = NewAction(10 + cell, cell, "pick");
                    action["isInProgress"] = true;
                    picks.Add(action);
                }
                actions.Add(picks);
            }
            session["actions"] = actions;

            var bansInfo = new JsonObject();
            bansInfo["myTeamBans"] = new JsonArray();
            bansInfo["theirTeamBans"] = new JsonArray();
            session["bans"] = bansInfo;

            var timer = new JsonObject();
            timer["phase"] = draft ? "PLANNING" : "BAN_PICK";
            timer["adjustedTimeLeftInPhase"] = 30000L;
            timer["isInfinite"] = false;
            session["timer"] = timer;
            return session;
        }

        private static JsonObject NewAction(long id, long cell, string type)
        {
            var action = new JsonObject();
            action["id"] = id;
            action["actorCellId"] = cell;
            action["championId"] = 0L;
            action["completed"] = false;
            action["isInProgress"] = false;
            action["isAllyAction"] = cell < 5;
            action["type"] = type;
            return action;
        }

        // Moves the draft along, the way the client does.

        public void StartBans(long timeLeft = 30000)
        {
            Change(c =>
            {
                ((JsonObject) c.Session["timer"])["phase"] = "BAN_PICK";
                ((JsonObject) c.Session["timer"])["adjustedTimeLeftInPhase"] = timeLeft;
                foreach (var a in c.AllActions().Where(x => (string) x["type"] == "ban")) a["isInProgress"] = true;
            });
        }

        // Everybody else bans: cell -> champion.
        public void OthersBan(params long[] cellAndChampion)
        {
            Change(c =>
            {
                for (var i = 0; i < cellAndChampion.Length; i += 2)
                {
                    var action = c.AllActions().First(x => (string) x["type"] == "ban" && (long) x["actorCellId"] == cellAndChampion[i]);
                    c.ApplyAction(action, cellAndChampion[i + 1], true);
                }
            });
        }

        public void StartTurn(long actionId, long timeLeft = 30000)
        {
            Change(c =>
            {
                foreach (var a in c.AllActions().Where(x => (string) x["type"] == "ban" && !(bool) x["completed"]))
                {
                    a["completed"] = true;
                    a["isInProgress"] = false;
                }
                ((JsonObject) c.Session["timer"])["adjustedTimeLeftInPhase"] = timeLeft;
                c.Action(actionId)["isInProgress"] = true;
            });
        }

        // A lane swap: we get a new position, and the client swaps Smite like it does.
        public void SwapTo(string position, long spell1, long spell2)
        {
            Change(c =>
            {
                c.Me()["assignedPosition"] = position;
                c.Me()["spell1Id"] = spell1;
                c.Me()["spell2Id"] = spell2;
            });
        }

        public void SetMySpells(long spell1, long spell2)
        {
            lock (this)
            {
                Me()["spell1Id"] = spell1;
                Me()["spell2Id"] = spell2;
            }
        }

        public void OtherPicks(long actionId, long championId)
        {
            Change(c =>
            {
                var action = c.Action(actionId);
                action["isInProgress"] = true;
                c.ApplyAction(action, championId, true);
            });
        }
    }

    class AutopickTests
    {
        static int failures;
        static int? serverDelay;

        // Every status autopick showed in the current scenario, since later ones replace earlier ones quickly.
        static List<string> history = new List<string>();

        static bool Saw(string status)
        {
            lock (history) return history.Contains(status);
        }

        static JsonObject Setup(string role, string picksJson, string bansJson)
        {
            return (JsonObject) SimpleJson.DeserializeObject("{\"" + role + "\":{\"picks\":" + picksJson + ",\"bans\":" + bansJson + "}}");
        }

        static void Scenario(string name, JsonObject setup, Action<FakeClient, Autopick> run, int delay = 0, bool enable = true)
        {
            serverDelay = delay;
            var client = new FakeClient();
            var engine = new Autopick(client, null, null, () => Task.FromResult(serverDelay), null);
            client.Engine = engine;
            lock (history) history.Clear();
            engine.OnChanged += () => { lock (history) history.Add(engine.Status); };
            engine.SetRoles(setup);
            if (enable) engine.SetEnabled(true);

            try
            {
                run(client, engine);
                Console.WriteLine("PASS " + name);
            }
            catch (Exception e)
            {
                failures++;
                Console.WriteLine("FAIL " + name + "\n    " + e.Message);
                Console.WriteLine("    status: " + engine.Status);
                foreach (var call in client.Writes()) Console.WriteLine("    " + call);
            }
        }

        static void WaitFor(Func<bool> condition, string what, int ms = 3000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                if (condition()) return;
                Thread.Sleep(20);
            }
            throw new Exception("Timed out waiting for: " + what);
        }

        // Waits until autopick has handled everything so far.
        static void Settle(Autopick engine)
        {
            Thread.Sleep(80);
            WaitFor(() => !engine.Busy, "autopick to finish");
            Thread.Sleep(80);
            WaitFor(() => !engine.Busy, "autopick to finish");
        }

        static void Equal<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual)) throw new Exception(what + ": expected <" + expected + "> but got <" + actual + ">");
        }

        static void True(bool value, string what)
        {
            if (!value) throw new Exception(what);
        }

        static bool Wrote(FakeClient c, string call)
        {
            return c.Writes().Contains(call);
        }

        const string LEE_FULL = "[{\"championId\":64,\"skinId\":64001,\"spell1Id\":4,\"spell2Id\":11,\"runes\":{\"type\":\"recommended\"}},{\"championId\":91}]";

        static void Main()
        {
            Scenario("draft: hovers in planning, bans on the ban turn, locks in on the pick turn, then sets spells, skin and runes", Setup("jungle", LEE_FULL, "[157,238]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64}"), "hover Lee Sin");
                WaitFor(() => Saw("Hovering Lee Sin. Autopick locks it in on your turn."), "hover status");

                c.StartBans();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/1 {\"championId\":157,\"completed\":true}"), "ban Yasuo");
                WaitFor(() => Saw("Banned Yasuo."), "ban status");

                c.OthersBan(0, 29, 5, 32);
                c.OtherPicks(10, 875);
                c.OtherPicks(11, 103);
                c.OtherPicks(12, 238);
                c.StartTurn(13);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64,\"completed\":true}"), "lock in Lee Sin");
                WaitFor(() => e.Status.StartsWith("Lee Sin: "), "extras status");
                Equal("Lee Sin: Spells, skin and runes set.", e.Status, "status");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":4,\"spell2Id\":11}"), "spells set");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"selectedSkinId\":64001}"), "skin set");

                var temporary = c.Pages.First(x => (bool) x["isTemporary"]);
                Equal(temporary["id"], (object) c.CurrentPage, "selected page");
                Equal("Lee Sin - Conqueror", (string) temporary["name"], "page name");
                Equal("[8010,9111,9104,8299,8143,8135,5005,5008,5001]", SimpleJson.SerializeObject(temporary["selectedPerkIds"]), "runes");
                Equal("rec64", (string) temporary["runeRecommendationId"], "recommendation");
                Equal(2, c.Pages.Count(x => !(bool) x["isTemporary"]), "own pages untouched");
            });

            Scenario("an enemy bans the first choice: hovers and locks in the backup", Setup("jungle", LEE_FULL, "[157]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64}"), "hover Lee Sin");
                c.StartBans();
                c.OthersBan(5, 64);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":91}"), "hover Talon instead");
                c.StartTurn(13);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":91,\"completed\":true}"), "lock in Talon");
                Settle(e);
                True(!c.Writes().Any(x => x.Contains("my-selection")), "no skin or spells for Talon");
                Equal("Talon: Runes set.", e.Status, "status");
            });

            Scenario("a teammate wants the first choice and the first ban: autopick takes the backups", Setup("jungle", LEE_FULL, "[157,238]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Change(x => x.Member(0)["championPickIntent"] = 64L);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":91}"), "hover Talon");
                c.Change(x => x.Member(0)["championPickIntent"] = 157L);
                c.StartBans();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/1 {\"championId\":238,\"completed\":true}"), "ban Zed");
                True(!c.Writes().Any(x => x.Contains("\"championId\":157")), "never touched Yasuo");
            });

            Scenario("the player hovers something else: autopick leaves the pick alone", Setup("jungle", LEE_FULL, "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64}"), "hover Lee Sin");
                Settle(e);
                c.Change(x => x.ApplyAction(x.Action(13), 103, false));
                WaitFor(() => e.Status == "You chose Ahri, so autopick left the pick to you.", "stand down status");
                c.StartBans();
                c.StartTurn(13);
                Settle(e);
                Thread.Sleep(500);
                True(!c.Writes().Any(x => x.Contains("completed")), "locked nothing in");
            });

            Scenario("the player bans something else during the delay: autopick leaves the ban alone", Setup("jungle", LEE_FULL, "[157]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                c.StartBans();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/1 {\"championId\":157}"), "hover the ban");
                var seen = new List<string>();
                e.OnChanged += () => { lock (seen) seen.Add(e.Status); };
                c.Change(x => x.ApplyAction(x.Action(1), 238, false));
                WaitFor(() => { lock (seen) return seen.Contains("You chose Zed to ban, so autopick left the ban to you."); }, "stand down status");
                Thread.Sleep(2500);
                True(!c.Writes().Any(x => x.Contains("completed")), "banned nothing");
            }, delay: 2);

            Scenario("lock in delay from the server: waits, then locks in", Setup("jungle", LEE_FULL, "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64}"), "hover");
                Equal(2L, (long) ((JsonObject) SimpleJson.DeserializeObject(e.GetState()))["lockInDelay"], "delay in the state");
                c.StartBans();
                var start = DateTime.UtcNow;
                c.StartTurn(13);
                WaitFor(() => e.Status.StartsWith("Locking in Lee Sin in "), "countdown");
                Thread.Sleep(1200);
                True(!c.Writes().Any(x => x.Contains("completed")), "locked in too early");
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64,\"completed\":true}"), "lock in");
                var took = (DateTime.UtcNow - start).TotalSeconds;
                True(took >= 1.9 && took < 3, "locked in after " + took + "s");
            }, delay: 2);

            Scenario("a long delay still locks in before the turn runs out", Setup("jungle", LEE_FULL, "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                c.StartBans();
                var start = DateTime.UtcNow;
                c.StartTurn(13, 5000);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64,\"completed\":true}"), "lock in", 4000);
                var took = (DateTime.UtcNow - start).TotalSeconds;
                True(took >= 1.9 && took < 3, "locked in after " + took + "s");
            }, delay: 30);

            Scenario("switched off: does nothing", Setup("jungle", LEE_FULL, "[157]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                c.StartBans();
                c.StartTurn(13);
                Settle(e);
                Thread.Sleep(400);
                Equal(0, c.Writes().Count, "writes");
            }, enable: false);

            Scenario("switched on in the middle of champ select: picks right away", Setup("jungle", LEE_FULL, "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                c.StartBans();
                c.StartTurn(13);
                Settle(e);
                e.SetEnabled(true);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64,\"completed\":true}"), "lock in");
            }, enable: false);

            Scenario("the game starting switches autopick off", Setup("jungle", LEE_FULL, "[]"), (c, e) =>
            {
                var changes = 0;
                e.OnChanged += () => changes++;
                e.HandleEvent("/lol-gameflow/v1/gameflow-phase", "Update", "ChampSelect");
                True(e.Enabled, "still on in champ select");
                e.HandleEvent("/lol-gameflow/v1/gameflow-phase", "Update", "InProgress");
                True(!e.Enabled, "switched off");
                True(changes > 0, "told the phone");
            });

            Scenario("blind pick: no roles, so the Any role setup; locks in right away", Setup("any", "[{\"championId\":32}]", "[157]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", false);
                c.Publish();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/11 {\"championId\":32,\"completed\":true}"), "lock in Amumu");
                True(!c.Writes().Any(x => x.Contains("\"championId\":157")), "no ban");
            });

            Scenario("League Classic: picks and bans the Classic versions, and leaves out a skin that doesn't fit", Setup("jungle", LEE_FULL, "[157]"), (c, e) =>
            {
                c.Offered = FakeClient.NAMES.Keys.Select(x => x + 60000).ToArray();
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":60064}"), "hover Classic Lee Sin");
                WaitFor(() => Saw("Hovering Lee Sin (Classic). Autopick locks it in on your turn."), "status");
                c.StartBans();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/1 {\"championId\":60157,\"completed\":true}"), "ban Classic Yasuo");
                c.StartTurn(13);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":60064,\"completed\":true}"), "lock in Classic Lee Sin");
                WaitFor(() => e.Status.StartsWith("Lee Sin (Classic): "), "extras");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":4,\"spell2Id\":11}"), "spells set");
                True(!c.Writes().Any(x => x.Contains("selectedSkinId")), "no skin of normal Lee Sin");
            });

            Scenario("a real list of bannable champions is respected", Setup("jungle", LEE_FULL, "[157,238]"), (c, e) =>
            {
                c.Bannable = FakeClient.Arr(238, 29, 32);
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                c.StartBans();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/1 {\"championId\":238,\"completed\":true}"), "ban Zed, since Yasuo can't be banned");
            });

            Scenario("a League Classic champion in the setup: its normal version in normal queues", Setup("jungle", "[{\"championId\":60091}]", "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":91,\"completed\":true}"), "lock in Talon");
            });

            // Smite. The fake client refuses Smite off jungle and refuses spells without it on jungle.
            Func<string, string, JsonObject> spells = (role, pair) => Setup(role, "[{\"championId\":64,\"runes\":{\"type\":\"none\"},\"spell1Id\":" + pair.Split(',')[0] + ",\"spell2Id\":" + pair.Split(',')[1] + "}]", "[]");

            Scenario("jungle with Flash + Ignite: keeps Flash, Smite on the key the client has it (F)", spells("jungle", "4,14"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.SetMySpells(4, 11);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status.StartsWith("Lee Sin: "), "status");
                Equal("Lee Sin: Spells set.", e.Status, "status");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":4,\"spell2Id\":11}"), "Flash + Smite");
            });

            Scenario("jungle with Flash + Ignite, client has Smite on D: Smite stays on D", spells("jungle", "4,14"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.SetMySpells(11, 4);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status.StartsWith("Lee Sin: "), "status");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":11,\"spell2Id\":4}"), "Smite + Flash");
            });

            Scenario("jungle without Flash: keeps the first spell next to Smite", spells("jungle", "14,12"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.SetMySpells(4, 11);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status.StartsWith("Lee Sin: "), "status");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":14,\"spell2Id\":11}"), "Ignite + Smite");
            });

            Scenario("jungle setup with Smite on D is sent as it is", spells("jungle", "11,4"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.SetMySpells(4, 11);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status.StartsWith("Lee Sin: "), "status");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":11,\"spell2Id\":4}"), "Smite + Flash");
            });

            Scenario("All roles used as jungler: Flash + Smite", spells("any", "4,14"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.SetMySpells(4, 11);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status == "Lee Sin: Spells set.", "status");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":4,\"spell2Id\":11}"), "Flash + Smite");
            });

            Scenario("mid with an old Flash + Smite setup: Smite left out, the client's spell stays, and it says so", spells("middle", "4,11"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true, "middle");
                c.SetMySpells(4, 14);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status.StartsWith("Lee Sin: "), "status");
                Equal("Lee Sin: Spells set. Smite is only for junglers, so autopick left it out.", e.Status, "status");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":4,\"spell2Id\":14}"), "Flash + Ignite");
            });

            Scenario("a lane swap from jungle to bot after lock-in: spells and runes set again, without Smite", Setup("jungle", "[{\"championId\":64,\"skinId\":64001,\"spell1Id\":4,\"spell2Id\":11}]", "[]"), (c, e) =>
            {
                Autopick.SWAP_SETTLE_MS = 300;
                try
                {
                    c.Session = FakeClient.NewSession("a", true);
                    c.SetMySpells(4, 11);
                    c.StartBans();
                    c.StartTurn(13);
                    WaitFor(() => e.Status == "Lee Sin: Spells, skin and runes set.", "first setup");
                    lock (c.Calls) True(c.Calls.Any(x => x.Contains("/position/JUNGLE/")), "jungle runes");

                    // Bot has no setup of its own: the champion keeps the jungle one.
                    c.SwapTo("bottom", 4, 7);
                    WaitFor(() => Saw("Lee Sin: Spells and runes set. Smite is only for junglers, so autopick left it out."), "second setup", 5000);
                    lock (c.Calls) True(c.Calls.Any(x => x.Contains("/position/BOTTOM/")), "bot runes");
                    Equal("PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":4,\"spell2Id\":7}", c.Writes().Last(x => x.Contains("spell1Id")), "Flash + Heal on bot");
                    Equal(1, c.Writes().Count(x => x.Contains("selectedSkinId")), "skin sent once");
                }
                finally
                {
                    Autopick.SWAP_SETTLE_MS = 1500;
                }
            });

            Scenario("ARAM: Smite isn't in the mode, so the client's spell stays", spells("any", "4,11"), (c, e) =>
            {
                c.GameMode = "ARAM";
                c.Session = FakeClient.NewSession("a", false);
                c.Publish();
                WaitFor(() => e.Status.StartsWith("Lee Sin: "), "status");
                True(Wrote(c, "PATCH /lol-champ-select/v1/session/my-selection {\"spell1Id\":4,\"spell2Id\":7}"), "Flash + Heal");
                True(e.Status.Contains("aren't in this game mode"), e.Status);
            });

            Scenario("the client keeps other spells: says so", spells("jungle", "4,11"), (c, e) =>
            {
                c.IgnoreSpells = true;
                c.Session = FakeClient.NewSession("a", true);
                c.SetMySpells(11, 4);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status.StartsWith("Lee Sin: "), "status", 5000);
                Equal("Lee Sin: The League client kept other spells.", e.Status, "status");
            });

            Scenario("filled into a role with nothing set up: uses All roles for picks and bans", Setup("any", LEE_FULL, "[157]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64}"), "hover Lee Sin");
                c.StartBans();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/1 {\"championId\":157,\"completed\":true}"), "ban Yasuo");
                c.StartTurn(13);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64,\"completed\":true}"), "lock in Lee Sin");
            });

            var mixed = Setup("jungle", "[{\"championId\":91}]", "[]");
            mixed["any"] = Setup("any", LEE_FULL, "[238]")["any"];
            Scenario("a role with picks but no bans: its own picks, the bans of All roles", mixed, (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":91}"), "hover Talon");
                c.StartBans();
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/1 {\"championId\":238,\"completed\":true}"), "ban Zed");
                c.StartTurn(13);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":91,\"completed\":true}"), "lock in Talon");
                True(!c.Writes().Any(x => x.Contains("\"championId\":64")), "never used the All roles picks");
            });

            Scenario("nothing set up anywhere: says so", Setup("jungle", "[]", "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", false);
                c.Publish();
                WaitFor(() => e.Status == "Autopick has nothing set up for All roles.", "status");
            });

            Scenario("a role without a setup: says so and does nothing", Setup("top", LEE_FULL, "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                WaitFor(() => e.Status == "Autopick has nothing set up for Jungle or All roles.", "status");
                Settle(e);
                Equal(0, c.Writes().Count, "writes");
            });

            Scenario("nothing left to pick: says so and leaves the pick to the player", Setup("jungle", "[{\"championId\":64}]", "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                c.StartBans();
                c.OthersBan(5, 64);
                WaitFor(() => e.Status == "None of your champions for Jungle are available right now.", "planning status");
                c.StartTurn(13);
                WaitFor(() => e.Status == "None of your champions for Jungle are available. Pick one yourself.", "turn status");
                Settle(e);
                True(!c.Writes().Any(x => x.Contains("completed")), "locked nothing in");
            });

            Scenario("a new champ select after a dodge starts over", Setup("jungle", LEE_FULL, "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.Publish();
                c.Change(x => x.ApplyAction(x.Action(13), 103, false));
                WaitFor(() => e.Status.StartsWith("You chose Ahri"), "stand down");
                e.HandleEvent("/lol-champ-select/v1/session", "Delete", null);
                Equal("", e.Status, "status cleared");
                True(e.Enabled, "still on after a dodge");
                c.Session = FakeClient.NewSession("b", true);
                c.Publish();
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => Wrote(c, "PATCH /lol-champ-select/v1/session/actions/13 {\"championId\":64,\"completed\":true}"), "lock in");
            });

            Scenario("runes from one of the player's pages: selects it, writes nothing", Setup("jungle", "[{\"championId\":64,\"runes\":{\"type\":\"page\",\"pageId\":2}}]", "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status == "Lee Sin: Runes set.", "status");
                Equal(2L, c.CurrentPage, "selected page");
                True(!c.Writes().Any(x => x.StartsWith("PUT /lol-perks/v1/pages") || x.StartsWith("POST /lol-perks")), "wrote no page");
            });

            const string CUSTOM = "[{\"championId\":64,\"runes\":{\"type\":\"custom\",\"primaryStyleId\":8100,\"subStyleId\":8000,\"selectedPerkIds\":[8112,8143,8138,8135,9111,8014,5008,5008,5001]}}]";

            Scenario("custom runes: goes into a new temporary page", Setup("jungle", CUSTOM, "[]"), (c, e) =>
            {
                c.Session = FakeClient.NewSession("a", true);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status == "Lee Sin: Runes set.", "status");
                var page = c.Pages.First(x => (long) x["id"] == c.CurrentPage);
                True((bool) page["isTemporary"], "temporary");
                Equal("Mimic Lee Sin", (string) page["name"], "name");
                Equal("[8112,8143,8138,8135,9111,8014,5008,5008,5001]", SimpleJson.SerializeObject(page["selectedPerkIds"]), "runes");
            });

            Scenario("an existing temporary page gets reused", Setup("jungle", CUSTOM, "[]"), (c, e) =>
            {
                c.Pages.Add(FakeClient.Page(555, "Amumu - old", true));
                c.Session = FakeClient.NewSession("a", true);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status == "Lee Sin: Runes set.", "status");
                Equal(555L, c.CurrentPage, "selected page");
                True(!c.Writes().Any(x => x.StartsWith("POST /lol-perks")), "created no page");
            });

            Scenario("no room for a temporary page: uses the FOR_MIMIC page and keeps its name", Setup("jungle", CUSTOM, "[]"), (c, e) =>
            {
                c.RefuseTemporaryPages = true;
                c.Pages.Add(FakeClient.Page(3, "for_mimic", false));
                c.Session = FakeClient.NewSession("a", true);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status == "Lee Sin: Runes set.", "status");
                Equal(3L, c.CurrentPage, "selected page");
                var page = c.Pages.First(x => (long) x["id"] == 3);
                Equal("for_mimic", (string) page["name"], "name kept");
                Equal("[8112,8143,8138,8135,9111,8014,5008,5008,5001]", SimpleJson.SerializeObject(page["selectedPerkIds"]), "runes");
                True(!c.Writes().Any(x => x.StartsWith("PUT /lol-perks/v1/pages/1") || x.StartsWith("PUT /lol-perks/v1/pages/2")), "other pages untouched");
            });

            Scenario("no room and no FOR_MIMIC page: says how to fix it, touches no page", Setup("jungle", CUSTOM, "[]"), (c, e) =>
            {
                c.RefuseTemporaryPages = true;
                c.Session = FakeClient.NewSession("a", true);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status.StartsWith("Lee Sin: "), "status");
                Equal("Lee Sin: No room for another rune page. Name one of your rune pages FOR_MIMIC and autopick will use it.", e.Status, "status");
                Equal(1L, c.CurrentPage, "selected page unchanged");
                True(!c.Writes().Any(x => x.StartsWith("PUT /lol-perks")), "touched no page");
            });

            Scenario("the client doesn't keep the runes: says so instead of claiming success", Setup("jungle", LEE_FULL, "[]"), (c, e) =>
            {
                c.DropRunes = true;
                c.Session = FakeClient.NewSession("a", true);
                c.StartBans();
                c.StartTurn(13);
                WaitFor(() => e.Status.Contains("keep the runes"), "status", 5000);
                Equal("Lee Sin: Spells and skin set. The League client didn't keep the runes.", e.Status, "status");
            });

            Scenario("setups are cleaned up: 4 picks, 2 bans, known roles and rune types only", null, (c, e) =>
            {
                var raw = "{\"jungle\":{\"picks\":[{\"championId\":1},{\"championId\":0},\"junk\",{\"championId\":2,\"runes\":{\"type\":\"custom\",\"selectedPerkIds\":[1,2]}},{\"championId\":3,\"runes\":{\"type\":\"weird\"}},{\"championId\":4},{\"championId\":5}],\"bans\":[7,0,8,9]},\"nonsense\":{}}";
                var roles = Autopick.NormalizeRoles(SimpleJson.DeserializeObject(raw));
                Equal("top,jungle,middle,bottom,utility,any", string.Join(",", roles.Keys), "roles");
                var jungle = (JsonObject) roles["jungle"];
                Equal("[{\"championId\":1,\"skinId\":0,\"spell1Id\":0,\"spell2Id\":0,\"runes\":{\"type\":\"recommended\"}},{\"championId\":2,\"skinId\":0,\"spell1Id\":0,\"spell2Id\":0,\"runes\":{\"type\":\"custom\",\"primaryStyleId\":0,\"subStyleId\":0,\"selectedPerkIds\":[1,2,0,0,0,0,0,0,0]}},{\"championId\":3,\"skinId\":0,\"spell1Id\":0,\"spell2Id\":0,\"runes\":{\"type\":\"recommended\"}},{\"championId\":4,\"skinId\":0,\"spell1Id\":0,\"spell2Id\":0,\"runes\":{\"type\":\"recommended\"}}]", SimpleJson.SerializeObject(jungle["picks"]), "picks");
                Equal("[7,8]", SimpleJson.SerializeObject(jungle["bans"]), "bans");
            });

            Scenario("requests from the phone", Setup("jungle", LEE_FULL, "[]"), (c, e) =>
            {
                var state = e.HandleRequest("GET", Autopick.PATH, null);
                Equal(200, state.Status, "get");
                Equal(false, ((JsonObject) state.Content)["enabled"], "off at first");

                Equal(200, e.HandleRequest("PUT", Autopick.PATH + "/enabled", "true").Status, "switch on");
                True(e.Enabled, "on");
                Equal(400, e.HandleRequest("PUT", Autopick.PATH + "/enabled", "\"yes\"").Status, "bad switch");
                Equal(400, e.HandleRequest("PUT", Autopick.PATH + "/roles", "[1]").Status, "bad setup");
                Equal(400, e.HandleRequest("PUT", Autopick.PATH + "/roles", "{broken").Status, "broken setup");
                Equal(404, e.HandleRequest("DELETE", Autopick.PATH, null).Status, "unknown");

                var saved = new List<string>();
                var engine = new Autopick(c, null, saved.Add, null, null);
                var result = engine.HandleRequest("PUT", Autopick.PATH + "/roles", "{\"top\":{\"picks\":[{\"championId\":875}],\"bans\":[157]}}");
                Equal(200, result.Status, "put setup");
                Equal(1, saved.Count, "saved");
                var reloaded = new Autopick(c, saved[0], null, null, null);
                Equal("[157]", SimpleJson.SerializeObject(((JsonObject) ((JsonObject) SimpleJson.DeserializeObject(reloaded.GetState()))["roles"])["top"] is JsonObject o ? o["bans"] : null), "setup survives a restart");
                Equal(false, ((JsonObject) SimpleJson.DeserializeObject(reloaded.GetState()))["enabled"], "the switch starts off after a restart");
            }, enable: false);

            Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            Environment.Exit(failures == 0 ? 0 : 1);
        }
    }
}
