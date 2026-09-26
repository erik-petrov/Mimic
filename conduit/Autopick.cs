using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Conduit
{
    /**
     * The result of a request to the League client: the HTTP status and the parsed JSON body.
     */
    public class ApiResult
    {
        public int Status;
        public object Content;

        public bool Ok => Status >= 200 && Status < 300;
    }

    /**
     * The part of the League client that autopick needs. Lets the tests use a fake client.
     */
    public interface ILeagueApi
    {
        Task<ApiResult> Request(string method, string path, string body);
    }

    /**
     * Picks and bans champions in champ select by itself, following the setup made on the phone
     * for each role. The phone switches it on, and it switches itself off when a game starts, so
     * it never acts in a later game by accident.
     *
     * Every role has up to four champions to pick (a first choice and three backups) and up to
     * two champions to ban. Each champion to pick can come with a skin, summoner spells and runes.
     */
    public class Autopick
    {
        // The path the phone uses for autopick. Requests to it never reach the League client.
        public const string PATH = "/mimic/v1/autopick";

        public static readonly string[] ROLES = { "top", "jungle", "middle", "bottom", "utility", "any" };
        public const int MAX_PICKS = 4;
        public const int MAX_BANS = 2;

        // If the client has no room for a temporary rune page, autopick writes into the page with this name.
        public const string FALLBACK_PAGE_NAME = "FOR_MIMIC";

        // League Classic champions are the same champions with this added to their id. Only
        // League Classic offers them, and it offers only them.
        public const long CLASSIC_OFFSET = 60000;

        // Smite belongs to the jungler: the client refuses it for other positions and won't take it
        // off a jungler. Flash is what a jungler keeps next to it.
        public const long SMITE = 11;
        public const long FLASH = 4;

        private static readonly Dictionary<string, string> ROLE_NAMES = new Dictionary<string, string>
        {
            { "top", "Top" }, { "jungle", "Jungle" }, { "middle", "Mid" }, { "bottom", "Bot" }, { "utility", "Support" }, { "any", "All roles" }
        };

        private readonly ILeagueApi api;
        private readonly Func<Task<int?>> fetchLockInDelay;
        private readonly Action<string> saveRoles;
        private readonly Action<string> log;

        private readonly object sync = new object();
        private bool enabled;
        private JsonObject roles;
        private int lockInDelay;
        private string status = "";

        private JsonObject session;
        private SessionProgress progress;
        private bool dirty;
        private bool running;
#pragma warning disable 414 // only kept so the timer isn't garbage collected
        private Timer ticker;
#pragma warning restore 414
        private Dictionary<long, string> championNames;
        private Dictionary<long, List<string>> spellModes;

        /**
         * Invoked whenever the state the phone shows changes.
         */
        public event Action OnChanged;

        /**
         * Creates the autopick engine. `savedRoles` is the setup stored by `saveRoles` before, or null.
         * `fetchLockInDelay` gets the number of seconds to wait before locking in, or null if unknown.
         */
        public Autopick(ILeagueApi api, string savedRoles, Action<string> saveRoles, Func<Task<int?>> fetchLockInDelay, Action<string> log)
        {
            this.api = api;
            this.saveRoles = saveRoles;
            this.fetchLockInDelay = fetchLockInDelay;
            this.log = log ?? (x => { });

            object parsed = null;
            try
            {
                if (!string.IsNullOrEmpty(savedRoles)) parsed = SimpleJson.DeserializeObject(savedRoles);
            }
            catch
            {
                // A broken file means no setup.
            }
            roles = NormalizeRoles(parsed);

            // Checks regularly so that a pick can be locked in after the delay.
            ticker = new Timer(_ => { if (session != null && enabled) Kick(); }, null, 300, 300);
        }

        public bool Enabled
        {
            get { lock (sync) return enabled; }
        }

        public string Status
        {
            get { lock (sync) return status; }
        }

        /**
         * True while autopick is working on the latest champ select state. Used by the tests.
         */
        public bool Busy
        {
            get { lock (sync) return running || dirty; }
        }

        /**
         * Returns what the phone shows: the switch, the setup, the lock in delay and what autopick is doing.
         */
        public string GetState()
        {
            lock (sync)
            {
                var state = new JsonObject();
                state["enabled"] = enabled;
                state["roles"] = roles;
                state["lockInDelay"] = lockInDelay;
                state["status"] = status;
                state["inChampSelect"] = session != null;
                return SimpleJson.SerializeObject(state);
            }
        }

        /**
         * Switches autopick on or off.
         */
        public void SetEnabled(bool value)
        {
            lock (sync)
            {
                if (enabled == value) return;
                enabled = value;
                status = "";
            }

            log("Autopick " + (value ? "on" : "off"));
            Changed();
            if (value)
            {
                RefreshLockInDelay();
                Kick();
            }
        }

        /**
         * Replaces the setup of every role and stores it.
         */
        public void SetRoles(object value)
        {
            var normalized = NormalizeRoles(value);
            lock (sync) roles = normalized;

            try
            {
                saveRoles?.Invoke(SimpleJson.SerializeObject(normalized));
            }
            catch (Exception e)
            {
                log("Could not save the autopick setup: " + e.Message);
            }

            Changed();
            Kick();
        }

        /**
         * Handles a request from the phone to PATH. Returns the status and the JSON to answer with.
         */
        public ApiResult HandleRequest(string method, string path, string body)
        {
            try
            {
                if (method == "GET" && path == PATH)
                {
                    return State();
                }

                if (method == "PUT" && path == PATH + "/enabled")
                {
                    var value = SimpleJson.DeserializeObject(body ?? "");
                    if (!(value is bool)) return Error(400, "Send true or false.");
                    SetEnabled((bool) value);
                    return State();
                }

                if (method == "PUT" && path == PATH + "/roles")
                {
                    var value = SimpleJson.DeserializeObject(body ?? "");
                    if (!(value is JsonObject)) return Error(400, "Send an object with the setup of every role.");
                    SetRoles(value);
                    return State();
                }
            }
            catch (Exception e)
            {
                return Error(400, "Could not read the request: " + e.Message);
            }

            return Error(404, "Autopick has nothing at " + method + " " + path + ".");
        }

        /**
         * Handles an event from the League client.
         */
        public void HandleEvent(string path, string type, object data)
        {
            if (path == "/lol-champ-select/v1/session")
            {
                var value = type == "Delete" ? null : data as JsonObject;
                bool ended;
                lock (sync)
                {
                    ended = session != null && value == null;
                    session = value;
                    if (value == null)
                    {
                        progress = null;
                        status = "";
                    }
                }

                if (ended) Changed();
                if (value != null) Kick();
            }
            else if (path == "/lol-gameflow/v1/gameflow-phase")
            {
                // The game started: switch off, so that autopick doesn't act in the next game.
                var phase = data as string;
                if ((phase == "GameStart" || phase == "InProgress") && Enabled)
                {
                    log("The game started, autopick switches off.");
                    SetEnabled(false);
                }
            }
        }

        /**
         * Reads the current champ select and game state, for when Conduit connects in the middle of one.
         */
        public async Task Refresh()
        {
            var phase = await api.Request("GET", "/lol-gameflow/v1/gameflow-phase", null);
            if (phase.Ok) HandleEvent("/lol-gameflow/v1/gameflow-phase", "Update", phase.Content);

            var current = await api.Request("GET", "/lol-champ-select/v1/session", null);
            HandleEvent("/lol-champ-select/v1/session", current.Ok ? "Update" : "Delete", current.Ok ? current.Content : null);
        }

        /**
         * Forgets the champ select, for when the League client closes.
         */
        public void Reset()
        {
            HandleEvent("/lol-champ-select/v1/session", "Delete", null);
        }

        private ApiResult State()
        {
            return new ApiResult { Status = 200, Content = SimpleJson.DeserializeObject(GetState()) };
        }

        private static ApiResult Error(int status, string message)
        {
            var content = new JsonObject();
            content["message"] = message;
            return new ApiResult { Status = status, Content = content };
        }

        private void Changed()
        {
            try
            {
                OnChanged?.Invoke();
            }
            catch (Exception e)
            {
                log("Autopick listener failed: " + e);
            }
        }

        private void SetStatus(string value)
        {
            lock (sync)
            {
                if (status == value) return;
                status = value;
            }

            log("Autopick: " + value);
            Changed();
        }

        private async void RefreshLockInDelay()
        {
            if (fetchLockInDelay == null) return;

            try
            {
                var value = await fetchLockInDelay();
                if (value == null) return;

                bool changed;
                lock (sync)
                {
                    changed = lockInDelay != value.Value;
                    lockInDelay = Math.Max(0, value.Value);
                }
                if (changed) Changed();
            }
            catch (Exception e)
            {
                log("Could not get the lock in delay: " + e.Message);
            }
        }

        /**
         * Works on the latest champ select state, one step at a time. Calls that arrive while
         * a step runs make it run again with the newest state afterwards.
         */
        private void Kick()
        {
            lock (sync)
            {
                dirty = true;
                if (running) return;
                running = true;
            }

            Task.Run(async () =>
            {
                while (true)
                {
                    JsonObject current;
                    lock (sync)
                    {
                        if (!dirty)
                        {
                            running = false;
                            return;
                        }

                        dirty = false;
                        current = session;
                    }

                    try
                    {
                        await Step(current);
                    }
                    catch (Exception e)
                    {
                        log("Autopick step failed: " + e);
                    }
                }
            });
        }

        private async Task Step(JsonObject s)
        {
            if (s == null) return;

            bool on;
            JsonObject setup;
            int delay;
            SessionProgress p;
            lock (sync)
            {
                on = enabled;
                setup = roles;
                delay = lockInDelay;

                // A new champ select (after a dodge, for example) starts over.
                var key = Str(s, "id") + ":" + Num(s, "gameId");
                if (progress == null || progress.Key != key) progress = new SessionProgress(key);
                p = progress;
            }

            if (!on) return;

            var me = Num(s, "localPlayerCellId");
            var local = Members(s, "myTeam").FirstOrDefault(x => Num(x, "cellId") == me);
            if (local == null) return;

            // Use the setup of the role League gave us. "All roles" ("any") fills in whatever that
            // role doesn't have (picks and bans separately), and is all there is in queues without roles.
            var position = (Str(local, "assignedPosition") ?? "").ToLowerInvariant();
            var roleKey = ROLES.Contains(position) && position != "any" ? position : "any";
            var role = (JsonObject) setup[roleKey];
            var all = (JsonObject) setup["any"];

            var picks = ((JsonArray) role["picks"]).Cast<JsonObject>().ToList();
            var pickRoleName = ROLE_NAMES[roleKey];
            if (picks.Count == 0)
            {
                picks = ((JsonArray) all["picks"]).Cast<JsonObject>().ToList();
                pickRoleName = ROLE_NAMES["any"];
            }

            var bans = ((JsonArray) role["bans"]).Select(x => Num(x)).ToList();
            var banRoleName = ROLE_NAMES[roleKey];
            if (bans.Count == 0)
            {
                bans = ((JsonArray) all["bans"]).Select(x => Num(x)).ToList();
                banRoleName = ROLE_NAMES["any"];
            }

            // After a lane swap into a role without a setup, the locked-in champion keeps the setup it came with.
            if (picks.Count == 0 && bans.Count == 0 && p.ExtrasEntry == null)
            {
                SetStatus(roleKey == "any" ? "Autopick has nothing set up for All roles." : "Autopick has nothing set up for " + ROLE_NAMES[roleKey] + " or All roles.");
                return;
            }

            if (!p.Loaded)
            {
                p.Loaded = true;
                RefreshLockInDelay();
                p.Pickable = await IdSet("/lol-champ-select/v1/pickable-champion-ids");
                p.Bannable = await IdSet("/lol-champ-select/v1/bannable-champion-ids");

                // The client can answer [-1] ("no ban") for the whole champ select while every
                // champion can be banned. A list without champions says nothing, so ignore it.
                if (p.Pickable != null && !p.Pickable.Any(x => x > 0)) p.Pickable = null;
                if (p.Bannable != null && !p.Bannable.Any(x => x > 0)) p.Bannable = null;
                await LoadChampionNames();
            }

            var actions = Actions(s);
            var phase = Str(Get(s, "timer") as JsonObject, "phase") ?? "";

            // Use the version of each champion this champ select offers: the League Classic one in
            // League Classic, the normal one everywhere else.
            picks = picks.Select(x =>
            {
                var id = ForThisQueue(Num(x, "championId"), p.Pickable);
                if (id == Num(x, "championId")) return x;
                var copy = Merge(x, new JsonObject());
                copy["championId"] = id;
                return copy;
            }).ToList();
            // Without a list of bannable champions, the pickable ones tell which version this queue uses.
            var classicQueue = p.Pickable != null && p.Pickable.Any(x => x >= CLASSIC_OFFSET);
            bans = bans.Select(x => p.Bannable != null ? ForThisQueue(x, p.Bannable) : p.Pickable != null ? ToVersion(x, classicQueue) : x).ToList();

            // Champions that can't be picked or banned anymore.
            var banned = new HashSet<long>(actions.Where(a => Str(a, "type") == "ban" && Bool(a, "completed")).Select(a => Num(a, "championId")));
            var bansInfo = Get(s, "bans") as JsonObject;
            if (bansInfo != null)
            {
                foreach (var x in (Get(bansInfo, "myTeamBans") as JsonArray ?? new JsonArray())) banned.Add(Num(x));
                foreach (var x in (Get(bansInfo, "theirTeamBans") as JsonArray ?? new JsonArray())) banned.Add(Num(x));
            }

            var taken = new HashSet<long>(actions.Where(a => Str(a, "type") == "pick" && Bool(a, "completed") && Num(a, "actorCellId") != me).Select(a => Num(a, "championId")));
            foreach (var member in Members(s, "myTeam").Concat(Members(s, "theirTeam")))
            {
                if (Num(member, "cellId") != me) taken.Add(Num(member, "championId"));
            }

            // Champions our teammates want, so autopick doesn't take or ban them.
            var wanted = new HashSet<long>(Members(s, "myTeam").Where(x => Num(x, "cellId") != me).Select(x => Num(x, "championPickIntent")));
            foreach (var a in actions.Where(a => Str(a, "type") == "pick" && Bool(a, "isAllyAction") && Num(a, "actorCellId") != me))
            {
                wanted.Add(Num(a, "championId"));
            }

            Func<long, bool> canPick = c => c > 0 && (p.Pickable == null || p.Pickable.Contains(c)) && !banned.Contains(c) && !taken.Contains(c) && !wanted.Contains(c);
            var firstPick = picks.Select(x => Num(x, "championId")).FirstOrDefault(canPick);
            Func<long, bool> canBan = c => c > 0 && (p.Bannable == null || p.Bannable.Contains(c)) && !banned.Contains(c) && !taken.Contains(c) && !wanted.Contains(c) && c != firstPick;

            // Our ban, when it's our turn to ban.
            var myBan = actions.FirstOrDefault(a => Str(a, "type") == "ban" && Num(a, "actorCellId") == me && !Bool(a, "completed") && Bool(a, "isInProgress"));
            if (myBan != null && phase != "PLANNING" && !p.BanStoodDown && bans.Count > 0)
            {
                await Ban(s, p, myBan, bans, canBan, delay, banRoleName);
                return;
            }

            // Our pick: hover it before our turn, lock it in on our turn.
            var myPick = actions.FirstOrDefault(a => Str(a, "type") == "pick" && Num(a, "actorCellId") == me && !Bool(a, "completed"));
            if (myPick != null && !p.PickStoodDown && picks.Count > 0)
            {
                await Pick(s, p, myPick, picks, canPick, phase, delay, pickRoleName);
                return;
            }

            // Once we have a champion, set up its skin, spells and runes. Again after a lane swap,
            // since the spells (Smite) and recommended runes depend on the position.
            var locked = Num(local, "championId");
            var extrasKey = locked + ":" + position;
            if (locked > 0 && p.ExtrasFor != extrasKey)
            {
                var swapped = p.ExtrasFor != null && p.ExtrasFor.StartsWith(locked + ":");
                if (swapped)
                {
                    // Give the client a moment to move Smite itself, like it does on a swap.
                    if (p.SettleUntil == DateTime.MinValue) p.SettleUntil = DateTime.UtcNow.AddMilliseconds(SWAP_SETTLE_MS);
                    if (DateTime.UtcNow < p.SettleUntil) return;
                }
                p.SettleUntil = DateTime.MinValue;
                p.ExtrasFor = extrasKey;

                var entry = picks.FirstOrDefault(x => Num(x, "championId") == locked);
                if (entry == null && p.ExtrasEntry != null && Num(p.ExtrasEntry, "championId") == locked) entry = p.ExtrasEntry;
                if (entry != null)
                {
                    p.ExtrasEntry = entry;
                    await ApplyExtras(entry, locked, position, Num(local, "spell1Id"), Num(local, "spell2Id"), !swapped);
                }
            }
        }

        // How long to wait after a lane swap before setting spells again.
        public static int SWAP_SETTLE_MS = 1500;

        private async Task Ban(JsonObject s, SessionProgress p, JsonObject action, List<long> bans, Func<long, bool> canBan, int delay, string roleName)
        {
            var id = Num(action, "id");
            var current = Num(action, "championId");

            if (p.BanActionId != id)
            {
                p.BanActionId = id;
                p.BanDone = false;

                if (current != 0)
                {
                    StandDownBan(p, "You chose a ban yourself, so autopick left the ban to you.");
                    return;
                }

                var choice = bans.FirstOrDefault(canBan);
                if (choice == 0)
                {
                    StandDownBan(p, "None of your bans for " + roleName + " can be banned. Ban a champion yourself.");
                    return;
                }

                p.BanLockAt = LockTime(s, delay);
                if (!await Hover(p, id, choice)) return;
                SetStatus(delay > 0 ? "Banning " + Name(choice) + " in " + delay + "s." : "Banning " + Name(choice) + ".");
                return;
            }

            if (p.BanDone) return;

            if (current != 0 && !p.Hovered.Contains(current))
            {
                StandDownBan(p, "You chose " + Name(current) + " to ban, so autopick left the ban to you.");
                return;
            }

            if (DateTime.UtcNow < p.BanLockAt) return;

            var target = canBan(current) ? current : bans.FirstOrDefault(canBan);
            if (target == 0)
            {
                StandDownBan(p, "None of your bans for " + roleName + " can be banned. Ban a champion yourself.");
                return;
            }

            p.BanDone = true;
            var result = await Complete(id, target);
            SetStatus(result.Ok ? "Banned " + Name(target) + "." : "The League client refused to ban " + Name(target) + Describe(result) + ".");
        }

        private async Task Pick(JsonObject s, SessionProgress p, JsonObject action, List<JsonObject> picks, Func<long, bool> canPick, string phase, int delay, string roleName)
        {
            var id = Num(action, "id");
            var current = Num(action, "championId");

            // The player picked something else, on the phone or at the PC.
            if (current != 0 && !p.Hovered.Contains(current))
            {
                p.PickStoodDown = true;
                SetStatus("You chose " + Name(current) + ", so autopick left the pick to you.");
                return;
            }

            var choice = picks.Select(x => Num(x, "championId")).FirstOrDefault(canPick);

            if (!Bool(action, "isInProgress") || phase != "BAN_PICK")
            {
                // Not our turn yet: show the team what we'll pick.
                if (choice == 0)
                {
                    SetStatus("None of your champions for " + roleName + " are available right now.");
                    return;
                }

                if (choice != current)
                {
                    if (!await Hover(p, id, choice)) return;
                }
                SetStatus("Hovering " + Name(choice) + ". Autopick locks it in on your turn.");
                return;
            }

            // Our turn.
            if (choice == 0)
            {
                p.PickStoodDown = true;
                SetStatus("None of your champions for " + roleName + " are available. Pick one yourself.");
                return;
            }

            if (p.PickActionId != id)
            {
                p.PickActionId = id;
                p.PickLockAt = LockTime(s, delay);
            }

            if (choice != current)
            {
                if (!await Hover(p, id, choice)) return;
            }

            var wait = p.PickLockAt - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                SetStatus("Locking in " + Name(choice) + " in " + Math.Ceiling(wait.TotalSeconds) + "s.");
                return;
            }

            p.PickStoodDown = true;
            var result = await Complete(id, choice);
            SetStatus(result.Ok ? "Locked in " + Name(choice) + "." : "The League client refused to lock in " + Name(choice) + Describe(result) + ".");
        }

        private void StandDownBan(SessionProgress p, string message)
        {
            p.BanStoodDown = true;
            SetStatus(message);
        }

        /**
         * Shows the specified champion for the specified action, without locking it in. Returns
         * whether the client took it. Doesn't send the same request twice within 2 seconds, since
         * the client's update can arrive after the next step already ran.
         */
        private async Task<bool> Hover(SessionProgress p, long actionId, long championId)
        {
            p.Hovered.Add(championId);
            if (p.LastHoverAction == actionId && p.LastHoverChampion == championId && DateTime.UtcNow - p.LastHoverAt < TimeSpan.FromSeconds(2)) return true;

            p.LastHoverAction = actionId;
            p.LastHoverChampion = championId;
            p.LastHoverAt = DateTime.UtcNow;

            var result = await api.Request("PATCH", "/lol-champ-select/v1/session/actions/" + actionId, "{\"championId\":" + championId + "}");
            if (!result.Ok) log("Hovering " + championId + " failed" + Describe(result));
            return result.Ok;
        }

        private Task<ApiResult> Complete(long actionId, long championId)
        {
            return api.Request("PATCH", "/lol-champ-select/v1/session/actions/" + actionId, "{\"championId\":" + championId + ",\"completed\":true}");
        }

        /**
         * When to lock in for a turn that starts now: after the delay, but always a few seconds
         * before the turn runs out.
         */
        private static DateTime LockTime(JsonObject s, int delay)
        {
            var timer = Get(s, "timer") as JsonObject;
            var wait = delay * 1000L;
            if (timer != null && !Bool(timer, "isInfinite"))
            {
                var left = Num(timer, "adjustedTimeLeftInPhase");
                if (left > 0) wait = Math.Min(wait, Math.Max(0, left - 3000));
            }

            return DateTime.UtcNow.AddMilliseconds(wait);
        }

        /**
         * Sets the skin, summoner spells and runes chosen for the champion we got.
         */
        private async Task ApplyExtras(JsonObject entry, long championId, string position, long have1, long have2, bool withSkin)
        {
            var done = new List<string>();
            var problems = new List<string>();

            var want1 = Num(entry, "spell1Id");
            var want2 = Num(entry, "spell2Id");
            if (want1 > 0 && want2 > 0)
            {
                var spells = await ResolveSpells(want1, want2, position, have1, have2);
                if (spells.Item3 != null) problems.Add(spells.Item3);
                if (spells.Item1 > 0 && spells.Item2 > 0)
                {
                    var result = await api.Request("PATCH", "/lol-champ-select/v1/session/my-selection", "{\"spell1Id\":" + spells.Item1 + ",\"spell2Id\":" + spells.Item2 + "}");
                    if (!result.Ok) problems.Add("the League client refused the spells" + Describe(result));
                    else if (await VerifySpells(spells.Item1, spells.Item2)) done.Add("spells");
                    else problems.Add("the League client kept other spells");
                }
            }

            // Skin ids are the champion id followed by three digits, so a skin chosen for the normal
            // version of a champion doesn't fit its League Classic version.
            var skin = Num(entry, "skinId");
            if (withSkin && skin > 0 && skin / 1000 == championId)
            {
                var result = await api.Request("PATCH", "/lol-champ-select/v1/session/my-selection", "{\"selectedSkinId\":" + skin + "}");
                if (result.Ok) done.Add("skin");
                else problems.Add("the League client refused the skin" + Describe(result));
            }

            var runes = await ApplyRunes(Get(entry, "runes") as JsonObject, championId, position);
            if (runes == "") done.Add("runes");
            else if (runes != null) problems.Add(runes);

            if (done.Count == 0 && problems.Count == 0) return;

            var message = Name(championId) + ": ";
            if (done.Count > 0) message += JoinList(done) + " set. ";
            if (problems.Count > 0) message += Capitalize(string.Join(", ", problems)) + ".";
            SetStatus(message.Trim());
        }

        /**
         * Works out which spells to send, following the client's Smite rule and the game mode.
         * A jungler always has Smite: if the setup has none, autopick keeps Flash (or the first
         * spell) and puts Smite on the key the client already has it on. Other positions never
         * get Smite. A spell that is left out is replaced by the spell already on that key.
         * Returns the spells (0 and 0 to send nothing) and a note about what was changed.
         */
        private async Task<Tuple<long, long, string>> ResolveSpells(long want1, long want2, string position, long have1, long have2)
        {
            string note = null;

            if (position == "jungle")
            {
                if (want1 != SMITE && want2 != SMITE)
                {
                    var keep = want1 == FLASH || want2 == FLASH ? FLASH : want1;
                    var smiteFirst = have1 == SMITE ? true : have2 == SMITE ? false : keep == want2;
                    want1 = smiteFirst ? SMITE : keep;
                    want2 = smiteFirst ? keep : SMITE;
                }
            }
            else if (position != "" && (want1 == SMITE || want2 == SMITE))
            {
                note = "Smite is only for junglers, so autopick left it out";
                if (want1 == SMITE) want1 = have1 != SMITE && have1 != want2 ? have1 : 0;
                if (want2 == SMITE) want2 = have2 != SMITE && have2 != want1 ? have2 : 0;
            }

            // Spells this game mode doesn't have, like Smite in ARAM.
            var mode = await GameMode();
            await LoadSpellModes();
            Func<long, bool> inMode = id => mode == null || spellModes == null || !spellModes.ContainsKey(id) || spellModes[id].Contains(mode);
            if (want1 > 0 && !inMode(want1))
            {
                note = note ?? "some of your spells aren't in this game mode, so autopick left them out";
                want1 = have1 != want2 && inMode(have1) ? have1 : 0;
            }
            if (want2 > 0 && !inMode(want2))
            {
                note = note ?? "some of your spells aren't in this game mode, so autopick left them out";
                want2 = have2 != want1 && inMode(have2) ? have2 : 0;
            }

            if (want1 <= 0 || want2 <= 0 || want1 == want2) return Tuple.Create(0L, 0L, note);
            return Tuple.Create(want1, want2, note);
        }

        /**
         * Checks that the client has the spells, instead of assuming it.
         */
        private async Task<bool> VerifySpells(long spell1, long spell2)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var check = await api.Request("GET", "/lol-champ-select/v1/session", null);
                var session = check.Ok ? check.Content as JsonObject : null;
                if (session != null)
                {
                    var me = Num(session, "localPlayerCellId");
                    var local = Members(session, "myTeam").FirstOrDefault(x => Num(x, "cellId") == me);
                    if (local != null && Num(local, "spell1Id") == spell1 && Num(local, "spell2Id") == spell2) return true;
                }
                await Task.Delay(200);
            }
            return false;
        }

        private async Task<string> GameMode()
        {
            var gameflow = await api.Request("GET", "/lol-gameflow/v1/session", null);
            var queue = Get(Get(gameflow.Content as JsonObject, "gameData") as JsonObject, "queue") as JsonObject;
            var mode = Str(queue, "gameMode");
            return string.IsNullOrEmpty(mode) ? null : mode;
        }

        private async Task LoadSpellModes()
        {
            if (spellModes != null) return;

            var result = await api.Request("GET", "/lol-game-data/assets/v1/summoner-spells.json", null);
            var list = result.Content as JsonArray;
            if (!result.Ok || list == null) return;

            var modes = new Dictionary<long, List<string>>();
            foreach (var spell in list.OfType<JsonObject>())
            {
                modes[Num(spell, "id")] = (Get(spell, "gameModes") as JsonArray ?? new JsonArray()).OfType<string>().ToList();
            }
            spellModes = modes;
        }

        /**
         * Applies the runes chosen for a champion. Returns "" if they were set, null if there is
         * nothing to set, or what went wrong.
         */
        private async Task<string> ApplyRunes(JsonObject setup, long championId, string position)
        {
            var type = Str(setup, "type") ?? "recommended";
            if (type == "none") return null;

            if (type == "page")
            {
                var pageId = Num(setup, "pageId");
                var pages = await api.Request("GET", "/lol-perks/v1/pages", null);
                var page = (pages.Content as JsonArray ?? new JsonArray()).OfType<JsonObject>().FirstOrDefault(x => Num(x, "id") == pageId);
                if (page == null) return "your chosen rune page doesn't exist anymore";

                var selected = await api.Request("PUT", "/lol-perks/v1/currentpage", pageId.ToString());
                if (!selected.Ok) return "the League client wouldn't select your rune page" + Describe(selected);
                return await Verify(pageId, null);
            }

            var runes = new JsonObject();
            string recommendationId = null;

            if (type == "custom")
            {
                var perks = (Get(setup, "selectedPerkIds") as JsonArray ?? new JsonArray()).Select(x => Num(x)).ToList();
                if (perks.Count != 9 || perks.Contains(0)) return "the custom runes aren't complete";

                runes["name"] = "Mimic " + Name(championId);
                runes["primaryStyleId"] = Num(setup, "primaryStyleId");
                runes["subStyleId"] = Num(setup, "subStyleId");
                runes["selectedPerkIds"] = ToArray(perks);
            }
            else
            {
                var mapId = 11L;
                var gameflow = await api.Request("GET", "/lol-gameflow/v1/session", null);
                var map = gameflow.Content is JsonObject ? Get((JsonObject) gameflow.Content, "map") as JsonObject : null;
                if (map != null && Num(map, "id") > 0) mapId = Num(map, "id");

                var pos = position == "" ? "NONE" : position.ToUpperInvariant();
                var result = await api.Request("GET", "/lol-perks/v1/recommended-pages/champion/" + championId + "/position/" + pos + "/map/" + mapId, null);
                var recommendation = (result.Content as JsonArray ?? new JsonArray()).OfType<JsonObject>().FirstOrDefault(x => Get(x, "keystone") is JsonObject && Get(x, "perks") is JsonArray);
                if (recommendation == null) return "the League client has no recommended runes for " + Name(championId) + Describe(result);

                recommendationId = Str(recommendation, "recommendationId");
                runes["name"] = Name(championId) + " - " + Str((JsonObject) recommendation["keystone"], "name");
                runes["primaryStyleId"] = Num(recommendation, "primaryPerkStyleId");
                runes["subStyleId"] = Num(recommendation, "secondaryPerkStyleId");
                runes["selectedPerkIds"] = ToArray(((JsonArray) recommendation["perks"]).OfType<JsonObject>().Select(x => Num(x, "id")));
            }

            var written = await WritePage(runes, championId, recommendationId);
            if (written.Item2 != null) return written.Item2;

            var select = await api.Request("PUT", "/lol-perks/v1/currentpage", written.Item1.ToString());
            if (!select.Ok) return "the League client wouldn't select the rune page" + Describe(select);

            return await Verify(written.Item1, ((JsonArray) runes["selectedPerkIds"]).Select(x => Num(x)).ToList());
        }

        /**
         * Writes runes into the temporary page, creating one if needed. If the client has no room,
         * uses the page named FOR_MIMIC. Returns the page id, or what went wrong.
         */
        private async Task<Tuple<long, string>> WritePage(JsonObject runes, long championId, string recommendationId)
        {
            var pages = (await api.Request("GET", "/lol-perks/v1/pages", null)).Content as JsonArray ?? new JsonArray();

            var temporary = pages.OfType<JsonObject>().FirstOrDefault(x => Bool(x, "isTemporary"));
            if (temporary != null)
            {
                var body = Merge(temporary, runes);
                body["recommendationChampionId"] = championId;
                body["runeRecommendationId"] = recommendationId ?? "";
                var saved = await api.Request("PUT", "/lol-perks/v1/pages/" + Num(temporary, "id"), SimpleJson.SerializeObject(body));
                if (saved.Ok) return Tuple.Create(Num(temporary, "id"), (string) null);
                log("Writing the temporary rune page failed" + Describe(saved));
            }
            else
            {
                var body = Merge(new JsonObject(), runes);
                body["isTemporary"] = true;
                body["recommendationChampionId"] = championId;
                body["runeRecommendationId"] = recommendationId ?? "";
                body["current"] = true;
                var created = await api.Request("POST", "/lol-perks/v1/pages", SimpleJson.SerializeObject(body));
                var id = created.Content is JsonObject ? Num((JsonObject) created.Content, "id") : 0;
                if (created.Ok && id > 0) return Tuple.Create(id, (string) null);
                log("Creating a temporary rune page failed" + Describe(created));
            }

            var fallback = pages.OfType<JsonObject>().FirstOrDefault(x => (Str(x, "name") ?? "").Trim().Equals(FALLBACK_PAGE_NAME, StringComparison.OrdinalIgnoreCase) && Bool(x, "isEditable"));
            if (fallback == null)
            {
                return Tuple.Create(0L, "no room for another rune page. Name one of your rune pages " + FALLBACK_PAGE_NAME + " and autopick will use it");
            }

            // Keep the name, so autopick finds the page next time.
            var update = Merge(fallback, runes);
            update["name"] = Str(fallback, "name");
            var result = await api.Request("PUT", "/lol-perks/v1/pages/" + Num(fallback, "id"), SimpleJson.SerializeObject(update));
            if (!result.Ok) return Tuple.Create(0L, "the League client refused the runes" + Describe(result));
            return Tuple.Create(Num(fallback, "id"), (string) null);
        }

        /**
         * Checks that the client has the page selected, with the runes, instead of assuming it.
         */
        private async Task<string> Verify(long pageId, List<long> perks)
        {
            JsonObject page = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var check = await api.Request("GET", "/lol-perks/v1/currentpage", null);
                page = check.Ok ? check.Content as JsonObject : null;
                if (page != null && Num(page, "id") == pageId && (perks == null || SameRunes(Get(page, "selectedPerkIds") as JsonArray, perks))) return "";
                await Task.Delay(200);
            }

            if (page == null || Num(page, "id") != pageId) return "the League client selected a different rune page";
            return "the League client didn't keep the runes";
        }

        private async Task<HashSet<long>> IdSet(string path)
        {
            var result = await api.Request("GET", path, null);
            var list = result.Content as JsonArray;
            return result.Ok && list != null ? new HashSet<long>(list.Select(x => Num(x))) : null;
        }

        private async Task LoadChampionNames()
        {
            if (championNames != null) return;

            var result = await api.Request("GET", "/lol-game-data/assets/v1/champion-summary.json", null);
            var list = result.Content as JsonArray;
            if (!result.Ok || list == null) return;

            var names = new Dictionary<long, string>();
            foreach (var champion in list.OfType<JsonObject>()) names[Num(champion, "id")] = Str(champion, "name");
            championNames = names;
        }

        private string Name(long championId)
        {
            string name;
            var classic = championId >= CLASSIC_OFFSET ? " (Classic)" : "";
            if (championNames != null && championNames.TryGetValue(championId, out name) && !string.IsNullOrEmpty(name)) return name + classic;
            return "champion " + championId;
        }

        /**
         * Returns the League Classic version of the champion if classic, else its normal version.
         */
        private static long ToVersion(long championId, bool classic)
        {
            if (classic && championId < CLASSIC_OFFSET) return championId + CLASSIC_OFFSET;
            if (!classic && championId >= CLASSIC_OFFSET) return championId - CLASSIC_OFFSET;
            return championId;
        }

        /**
         * Returns the champion, or its League Classic (or normal) version if only that one is offered.
         */
        private static long ForThisQueue(long championId, HashSet<long> offered)
        {
            if (offered == null || offered.Contains(championId)) return championId;
            var twin = championId >= CLASSIC_OFFSET ? championId - CLASSIC_OFFSET : championId + CLASSIC_OFFSET;
            return offered.Contains(twin) ? twin : championId;
        }

        /**
         * Brings a setup from the phone into the stored form, dropping anything unknown or broken.
         */
        public static JsonObject NormalizeRoles(object input)
        {
            var source = input as JsonObject;
            var result = new JsonObject();

            foreach (var key in ROLES)
            {
                object value = null;
                if (source != null) source.TryGetValue(key, out value);
                var role = value as JsonObject;

                var picks = new JsonArray();
                var bans = new JsonArray();
                object list;

                if (role != null && role.TryGetValue("picks", out list) && list is JsonArray)
                {
                    foreach (var item in ((JsonArray) list).OfType<JsonObject>())
                    {
                        var championId = Num(item, "championId");
                        if (championId <= 0 || picks.Count >= MAX_PICKS) continue;

                        var pick = new JsonObject();
                        pick["championId"] = championId;
                        pick["skinId"] = Math.Max(0, Num(item, "skinId"));
                        pick["spell1Id"] = Math.Max(0, Num(item, "spell1Id"));
                        pick["spell2Id"] = Math.Max(0, Num(item, "spell2Id"));
                        pick["runes"] = NormalizeRunes(Get(item, "runes") as JsonObject);
                        picks.Add(pick);
                    }
                }

                if (role != null && role.TryGetValue("bans", out list) && list is JsonArray)
                {
                    foreach (var item in (JsonArray) list)
                    {
                        var championId = Num(item);
                        if (championId > 0 && bans.Count < MAX_BANS) bans.Add(championId);
                    }
                }

                var normalized = new JsonObject();
                normalized["picks"] = picks;
                normalized["bans"] = bans;
                result[key] = normalized;
            }

            return result;
        }

        private static JsonObject NormalizeRunes(JsonObject input)
        {
            var type = Str(input, "type");
            if (type != "page" && type != "custom" && type != "none") type = "recommended";

            var runes = new JsonObject();
            runes["type"] = type;
            if (type == "page") runes["pageId"] = Num(input, "pageId");
            if (type == "custom")
            {
                runes["primaryStyleId"] = Num(input, "primaryStyleId");
                runes["subStyleId"] = Num(input, "subStyleId");
                var perks = (Get(input, "selectedPerkIds") as JsonArray ?? new JsonArray()).Select(x => Num(x)).Take(9).ToList();
                while (perks.Count < 9) perks.Add(0);
                runes["selectedPerkIds"] = ToArray(perks);
            }

            return runes;
        }

        private static List<JsonObject> Actions(JsonObject s)
        {
            var result = new List<JsonObject>();
            foreach (var group in (Get(s, "actions") as JsonArray ?? new JsonArray()).OfType<JsonArray>())
            {
                result.AddRange(group.OfType<JsonObject>());
            }
            return result;
        }

        private static IEnumerable<JsonObject> Members(JsonObject s, string team)
        {
            return (s[team] as JsonArray ?? new JsonArray()).OfType<JsonObject>();
        }

        private static JsonObject Merge(JsonObject a, JsonObject b)
        {
            var result = new JsonObject();
            foreach (var entry in a) result[entry.Key] = entry.Value;
            foreach (var entry in b) result[entry.Key] = entry.Value;
            return result;
        }

        private static JsonArray ToArray(IEnumerable<long> values)
        {
            var array = new JsonArray();
            foreach (var value in values) array.Add(value);
            return array;
        }

        private static bool SameRunes(JsonArray a, List<long> b)
        {
            if (a == null || a.Count != b.Count) return false;
            return a.Select(x => Num(x)).OrderBy(x => x).SequenceEqual(b.OrderBy(x => x));
        }

        private static string Describe(ApiResult result)
        {
            if (result.Ok) return "";
            var content = result.Content as JsonObject;
            var message = content != null ? Str(content, "message") : null;
            return " (error " + result.Status + (string.IsNullOrEmpty(message) ? "" : ": " + message) + ")";
        }

        private static string JoinList(List<string> items)
        {
            var text = items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items.Last();
            return Capitalize(text);
        }

        private static string Capitalize(string text)
        {
            return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static long Num(object value)
        {
            if (value is long) return (long) value;
            if (value is int) return (int) value;
            if (value is double) return (long) (double) value;
            return 0;
        }

        private static long Num(JsonObject obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) ? Num(value) : 0;
        }

        private static object Get(JsonObject obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) ? value : null;
        }

        private static string Str(JsonObject obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) ? value as string : null;
        }

        private static bool Bool(JsonObject obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) && value is bool && (bool) value;
        }

        /**
         * What autopick did in one champ select.
         */
        private class SessionProgress
        {
            public readonly string Key;

            public bool Loaded;
            public HashSet<long> Pickable;
            public HashSet<long> Bannable;

            // Champions autopick hovered, to tell them apart from the player's own choices.
            public readonly HashSet<long> Hovered = new HashSet<long>();
            public long LastHoverAction = -1;
            public long LastHoverChampion;
            public DateTime LastHoverAt;

            public long BanActionId = -1;
            public DateTime BanLockAt;
            public bool BanDone;
            public bool BanStoodDown;

            public long PickActionId = -1;
            public DateTime PickLockAt;
            public bool PickStoodDown;

            // Champion and position the skin, spells and runes were set for, and the setup used.
            public string ExtrasFor;
            public JsonObject ExtrasEntry;
            public DateTime SettleUntil = DateTime.MinValue;

            public SessionProgress(string key)
            {
                Key = key;
            }
        }
    }
}
