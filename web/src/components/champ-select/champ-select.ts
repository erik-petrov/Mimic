import Vue from "vue";
import Root, { Result } from "../root/root";
import { Component } from "vue-property-decorator";
import { loadStaticData, mapBackground } from "@/constants";

import Timer from "./timer.vue";
import Members from "./members.vue";
import PlayerSettings from "./player-settings.vue";
import SummonerPicker from "./summoner-picker.vue";
import ChampionPicker from "./champion-picker.vue";
import RuneEditor from "./rune-editor.vue";
import Bench from "./bench.vue";
import SkinPicker from "./skin-picker.vue";
import SwapPrompt from "./swap-prompt.vue";
import RuneRecommendations from "./rune-recommendations.vue";
import { setAutopickEnabled } from "../autopick/autopick-state";

import MagicBackground from "../../static/magic-background.jpg";

export interface ChampSelectMember {
    assignedPosition: string; // lowercase, e.g. "jungle". Empty in blind pick.
    playerType: string; // "BOT" for bots
    cellId: number;
    championId: number;
    championPickIntent: number;
    selectedSkinId: number;
    summonerId: number;
    gameName: string; // empty if the name is hidden
    tagLine: string;
    nameVisibilityType: string; // "VISIBLE" or "HIDDEN"
    spell1Id: number;
    spell2Id: number;
    team: number;

    // added manually
    displayName: string;
    isFriendly: boolean;
}

export interface ChampSelectAction {
    id: number;
    actorCellId: number;
    championId: number;
    completed: boolean;
    type: "ban" | "pick" | "ten_bans_reveal"; // might be more types, only these two are used in conventional queues
}

// A 'turn' is simply an array of actions that happen at the same time.
// In blind pick, this is all the players picking. In draft pick, every
// turn only contains a single action (since no players pick at the same time).
export type ChampSelectTurn = ChampSelectAction[];

export interface ChampSelectTimer {
    phase: "PLANNING" | "BAN_PICK" | "FINALIZATION" | "GAME_STARTING"; // might be more
    isInfinite: boolean;
    adjustedTimeLeftInPhase: number; // time left in ms
}

export interface ChampSelectState {
    actions: ChampSelectTurn[];

    localPlayerCellId: number;
    localPlayer: ChampSelectMember; // added manually, not actually in the payload

    myTeam: ChampSelectMember[];
    theirTeam: ChampSelectMember[];

    timer: ChampSelectTimer;

    // Swaps with teammates. Champion swaps are still reported under "trades".
    trades: SwapContract[];
    pickOrderSwaps: SwapContract[];
    positionSwaps: SwapContract[];

    benchEnabled: boolean;
    benchChampions: { championId: number, isPriority: boolean }[];
}

export type SwapState = "ACCEPTED" | "AVAILABLE" | "BUSY" | "CANCELLED" | "DECLINED" | "INVALID" | "RECEIVED" | "SENT";

// A possible swap with the teammate in the specified cell. The id changes after every swap.
export interface SwapContract {
    id: number;
    cellId: number;
    state: SwapState;
}

export type SwapKind = "position" | "pickOrder" | "champion";

const SWAP_ENDPOINTS: { [kind in SwapKind]: string } = {
    position: "position-swaps",
    pickOrder: "pick-order-swaps",
    champion: "champion-swaps"
};

// An entry from /lol-champ-select/v1/all-grid-champions, with the name in the client's language.
export interface GridChampion {
    id: number;
    name: string;
    owned: boolean;
    freeToPlay: boolean;
    disabled: boolean;
}

export interface GameflowState {
    map: { id: number };
    gameData: {
        queue: {
            gameMode: string;
            gameTypeConfig: {
                reroll: boolean;
            };
        };
    }
}

export interface RerollState {
    numberOfRolls: number;
    maxRolls: number;
}

export interface RunePage {
    id: number;
    name: string;
    isEditable: boolean;
    isActive: boolean;
    isTemporary?: boolean; // recommended pages made by the client, like auto runes
    recommendationChampionId?: number;
    order: number;
    primaryStyleId: number; // -1 if not selected
    subStyleId: number; // -1 if not selected
    selectedPerkIds: number[]; // 0 or not included if not selected
}

export interface SkinItem {
    championId: number;
    id: number;
    name: string;
    isBase: boolean;
    disabled: boolean;
    ownership: {
        owned: boolean
    };
}

@Component({
    components: {
        timer: Timer,
        members: Members,
        playerSettings: PlayerSettings,
        summonerPicker: SummonerPicker,
        championPicker: ChampionPicker,
        runeEditor: RuneEditor,
        bench: Bench,
        skinPicker: SkinPicker,
        swapPrompt: SwapPrompt,
        runeRecommendations: RuneRecommendations
    }
})
export default class ChampSelect extends Vue {
    $root: Root;

    state: ChampSelectState | null = null;
    gameflowState: GameflowState | null = null;
    rerollState: RerollState = { numberOfRolls: 0, maxRolls: 2 };

    runePages: RunePage[] = [];
    currentRunePage: RunePage | null = null;

    skins: SkinItem[] = [];
    pickingSkin = false;

    // Every champion the client knows about, by id. Loaded from the client itself so
    // that it always matches the client's patch and language.
    champions: { [id: number]: GridChampion } = {};
    loadingChampions = false;

    // Used to map summoner spell id -> data.
    summonerSpellDetails: { [id: number]: { id: string, key: string, name: string } } = {};

    // Information for the summoner spell overlay.
    pickingSummonerSpell = false;
    pickingFirstSummonerSpell = false;

    // Information for the champion picker.
    pickingChampion = false;

    // Information for the rune editor.
    showingRuneOverlay = false;

    // Information for the list of recommended rune pages.
    showingRecommendations = false;

    // Information for the reroll bench.
    showingBench = false;

    mounted() {
        this.loadStatic("summoner.json").then(map => {
            // map to { id: data }
            const details: any = {};
            Object.keys(map.data).forEach(x => details[+map.data[x].key] = map.data[x]);
            this.summonerSpellDetails = details;
        });

        // Start observing champion select.
        this.$root.observe("/lol-champ-select/v1/session", this.handleChampSelectChange.bind(this));

        // Keep track of reroll points for if we play ARAM.
        this.$root.observe("/lol-summoner/v1/current-summoner/rerollPoints", result => {
            this.rerollState = result.status === 200 ? result.content : { numberOfRolls: 0, maxRolls: 2 };
        });

        // Observe runes
        this.$root.observe("/lol-perks/v1/pages", response => {
            response.status === 200 && (this.runePages = response.content);
            response.status === 200 && (this.runePages.sort((a, b) => a.order - b.order));
        });

        this.$root.observe("/lol-perks/v1/currentpage", response => {
            this.currentRunePage = response.status === 200 ? response.content : null;

            // Update the isActive param if needed.
            if (this.currentRunePage) {
                this.runePages.forEach(x => x.isActive = x.id === this.currentRunePage!.id);
            }
        });
    }

    /**
     * Handles a change to the champion select and updates the state appropriately.
     * Note: this cannot be an arrow function for various changes. See the lobby component for more info.
     */
    handleChampSelectChange = async function(this: ChampSelect, result: Result) {
        if (result.status !== 200) {
            this.state = null;
            return;
        }
        
        const newState: ChampSelectState = result.content;
        newState.localPlayer = newState.myTeam.filter(x => x.cellId === newState.localPlayerCellId)[0];

        // The champion list only exists while in champ select, so load it when we enter.
        if (!this.state || !Object.keys(this.champions).length) {
            this.loadChampions();
        }

        // If we haven't loaded skins before, do so now.
        if (!this.skins.length) {
            const url = `/lol-champions/v1/inventories/${newState.localPlayer.summonerId}/skins-minimal`;
            this.skins = await this.$root.request(url).then(x => x.content);

            this.$root.observe(url, data => {
                this.skins = data.status === 200 ? data.content : []
            });
        }

        // The session includes Riot IDs. They are empty when the client hides the name,
        // such as for the enemy team, in which case we show a placeholder like the client does.
        newState.myTeam.forEach((mem, idx) => {
            mem.displayName = mem.playerType === "BOT"
                ? this.championName(mem.championId) + " Bot"
                : mem.gameName || "Summoner " + (idx + 1);
            mem.isFriendly = true;
        });

        newState.theirTeam.forEach((mem, idx) => {
            mem.displayName = mem.gameName || "Summoner " + (idx + 1);
            mem.isFriendly = false;
        });

        // If we weren't in champ select before, fetch some data.
        if (!this.state) {
            // Gameflow, which contains information about the map and gamemode we are queued up for.
            this.$root.request("/lol-gameflow/v1/session").then(x => {
                x.status === 200 && (this.gameflowState = <GameflowState>x.content);
            });
        }

        const oldAction = this.state ? this.getActions(this.state.localPlayer) : undefined;
        this.state = newState;

        const newAction = this.getActions(this.state.localPlayer);
        // If we didn't have an action and have one now, or if the actions differ in id, present the champion picker.
        if ((!oldAction && newAction) || (newAction && oldAction && oldAction.id !== newAction.id)) {
            this.pickingChampion = true;
        }
    };

    /**
     * @returns the map background for the current queue
     */
    get background(): string {
        if (!this.gameflowState) return "background-image: url(" + MagicBackground + ")";
        return mapBackground(this.gameflowState.map.id);
    }

    // TODO: Maybe unify the following two functions?
    /**
     * @returns the current turn happening, or null if no single turn is currently happening (pre and post picks)
     */
    get currentTurn(): ChampSelectTurn | null {
        if (!this.state || this.state.timer.phase !== "BAN_PICK") return null;
        // Find the first set of actions that has at least one not completed.
        return this.state.actions.filter(x => x.filter(y => !y.completed).length > 0)[0];
    }

    /**
     * @returns the next turn happening, or null if there is no next turn.
     */
    get nextTurn(): ChampSelectTurn | null {
        if (!this.state || this.state.timer.phase !== "BAN_PICK") return null;
        return this.state.actions.filter(x => x.filter(y => !y.completed).length > 0)[1];
    }

    /**
     * @returns the action in the current turn for the specified member, or null if the member can't do anything
     */
    getActions(member: ChampSelectMember, future = false): ChampSelectAction | null {
        const turn = future ? this.nextTurn : this.currentTurn;
        return turn ? turn.filter(x => x.actorCellId === member.cellId)[0] || null : null;
    }

    /**
     * @returns the member associated with the specified cellId
     */
    getMember(cellId: number): ChampSelectMember {
        if (!this.state) throw new Error("Shouldn't happen");
        return this.state.myTeam.filter(x => x.cellId === cellId)[0] || this.state.theirTeam.filter(x => x.cellId === cellId)[0];
    }

    /**
     * Loads the names of all champions from the client.
     */
    async loadChampions() {
        if (this.loadingChampions) return;

        this.loadingChampions = true;
        const result = await this.$root.request("/lol-champ-select/v1/all-grid-champions");
        this.loadingChampions = false;
        if (result.status !== 200 || !Array.isArray(result.content)) return;

        const champions: { [id: number]: GridChampion } = {};
        result.content.forEach((x: GridChampion) => champions[x.id] = x);
        this.champions = champions;
    }

    /**
     * @returns the name of the specified champion, in the client's language
     */
    championName(id: number): string {
        const champ = this.champions[id];
        return champ ? champ.name : "Unknown";
    }

    /**
     * @returns the swap of the specified kind with the teammate in the specified cell, if any
     */
    getSwap(kind: SwapKind, cellId: number): SwapContract | undefined {
        return this.getSwaps(kind).filter(x => x.cellId === cellId)[0];
    }

    /**
     * @returns all swaps of the specified kind in the current session
     */
    getSwaps(kind: SwapKind): SwapContract[] {
        if (!this.state) return [];
        if (kind === "position") return this.state.positionSwaps || [];
        if (kind === "pickOrder") return this.state.pickOrderSwaps || [];
        return this.state.trades || [];
    }

    /**
     * Requests, accepts, declines or cancels the specified swap.
     */
    swapAction(kind: SwapKind, swap: SwapContract, action: "request" | "accept" | "decline" | "cancel") {
        this.$root.request(`/lol-champ-select/v1/session/${SWAP_ENDPOINTS[kind]}/${swap.id}/${action}`, "POST");
    }

    /**
     * @returns the champion the local player has picked or is hovering, or 0 if none
     */
    /**
     * @returns what autopick is doing in this champ select, or "" to show nothing
     */
    get autopickStatus(): string {
        const autopick = this.$root.autopick;
        if (!autopick) return "";
        return autopick.status || (autopick.enabled ? "Autopick is on." : "");
    }

    /**
     * Switches autopick off, leaving the rest of this champ select to the player.
     */
    async stopAutopick() {
        const result = await setAutopickEnabled(this.$root, false);
        if (result.status !== 200) this.$root.showNotification("Could not stop autopick (error " + result.status + ").");
    }

    get localChampionId(): number {
        if (!this.state) return 0;

        const member = this.state.localPlayer;
        const act = this.getActions(member);
        return (act ? act.championId : 0) || member.championId || member.championPickIntent || 0;
    }

    /**
     * Opens the list of recommended rune pages for our champion.
     */
    openRecommendations() {
        if (!this.localChampionId) {
            this.$root.showNotification("Pick or hover a champion first, then tap the wand again.");
            return;
        }

        this.showingRecommendations = true;
    }

    /**
     * Selects the specified rune page, by setting its `current` property to true and calling the collections backend.
     */
    selectRunePage(event: Event) {
        const id = +(event.target as HTMLSelectElement).value;
        this.runePages.forEach(r => r.isActive = r.id === id);
        this.$root.request("/lol-perks/v1/currentpage", "PUT", "" + id);
    }

    /**
     * @returns whether the local summoner still needs to pick a champion or not
     */
    get hasLockedChampion(): boolean {
        return !!this.state // A state exists.
            && this.state.actions.filter(x => // and we cannot find a pick turn in which
                x.filter(y => // there exists an action...
                    y.actorCellId === this.state!.localPlayerCellId // by the current player
                    && y.type === "pick" // that has to pick a champion
                    && !y.completed // and hasn't been completed yet
                ).length > 0).length === 0;
    }

    /**
     * @returns whether everyone has picked their champion already
     */
    get hasEveryonePicked(): boolean {
        return !!this.state // A state exists.
            && this.state.actions.filter(x => // and we cannot find a pick turn in which
                x.filter(y => // there exists an action...
                    y.type === "pick" // that has to pick a champion
                    && !y.completed // and hasn't been completed yet
                ).length > 0).length === 0;
    }

    /**
     * Helper method to load the specified json name from the ddragon static data.
     */
    public loadStatic(filename: string): Promise<any> {
        return loadStaticData(filename);
    }
}