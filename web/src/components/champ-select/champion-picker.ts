import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import { ChampSelectAction, ChampSelectState, default as ChampSelect, SwapContract } from "./champ-select";
import Root from "../root/root";
import ChampionGrid from "../common/champion-grid.vue";
import { GridEntry } from "../common/champion-grid";

// ARAM: a champion we can get, from the bench or from a teammate who'd trade.
interface PoolChampion {
    championId: number;
    trade?: SwapContract;
}

@Component({
    components: { championGrid: ChampionGrid }
})
export default class ChampionPicker extends Vue {
    $root: Root;
    $parent: ChampSelect;

    @Prop()
    state: ChampSelectState;

    @Prop()
    show: boolean;

    // List of champions that the current user can select. Includes banned champions.
    pickableChampions: number[] = [];

    // List of champions that the current user can ban. Includes already banned champions.
    bannableChampions: number[] = [];

    // ARAM: the champion tapped in the grid, which the button then chooses, swaps for or asks for.
    tapped = 0;

    created() {
        // Observe the list of pickable and bannable champions. These are kept as-is and only
        // filtered and sorted when shown, so that names loading later can never drop champions.
        this.$root.observe("/lol-champ-select/v1/pickable-champion-ids", result => {
            if (result.status === 200 && Array.isArray(result.content)) this.pickableChampions = result.content;
        });

        this.$root.observe("/lol-champ-select/v1/bannable-champion-ids", result => {
            if (result.status === 200 && Array.isArray(result.content)) this.bannableChampions = result.content;
        });
    }

    destroyed() {
        this.$root.unobserve("/lol-champ-select/v1/pickable-champion-ids");
        this.$root.unobserve("/lol-champ-select/v1/bannable-champion-ids");
    }

    /**
     * @returns the selectable champions with their names, for the grid
     */
    get gridChampions(): GridEntry[] {
        if (this.mode === "dealt") {
            return this.$parent.dealtChampions.map(id => ({ id, name: this.championName(id) }));
        }

        if (this.mode === "pool") {
            return this.pool.map(x => ({
                id: x.championId,
                name: this.championName(x.championId),
                tag: !x.trade ? "Bench" : x.trade.state === "SENT" ? "Asked" : "Trade"
            }));
        }

        return this.selectableChampions.map(id => ({ id, name: this.championName(id) }));
    }

    /**
     * @returns what the picker is for: choosing one of the champions ARAM Mayhem dealt us,
     * swapping our ARAM champion, or the picks and bans of other queues
     */
    get mode(): "dealt" | "pool" | "normal" {
        if (!this.state) return "normal";
        if (this.$parent.choosingDealtChampion) return "dealt";
        return this.state.benchEnabled ? "pool" : "normal";
    }

    /**
     * @returns the champions we can get in ARAM: the bench, and teammates' champions they can trade us
     */
    get pool(): PoolChampion[] {
        if (!this.state) return [];

        const pool: PoolChampion[] = (this.state.benchChampions || [])
            .filter(x => x.championId > 0)
            .map(x => ({ championId: x.championId }));

        for (const trade of this.$parent.getSwaps("champion")) {
            if (trade.state !== "AVAILABLE" && trade.state !== "SENT") continue;

            // Each champion once, since the grid can't show one twice.
            const member = this.state.myTeam.filter(x => x.cellId === trade.cellId)[0];
            if (member && member.championId > 0 && member.cellId !== this.state.localPlayerCellId && !pool.some(x => x.championId === member.championId)) {
                pool.push({ championId: member.championId, trade });
            }
        }

        return pool;
    }

    /**
     * @returns the pool entry for the tapped champion, if it's still in the pool
     */
    get tappedEntry(): PoolChampion | undefined {
        return this.pool.filter(x => x.championId === this.tapped)[0];
    }

    /**
     * @returns the list of champion ids currently "selectable"
     */
    get selectableChampions(): number[] {
        if (!this.state) return [];
        const isCurrentlyBanning = this.$parent.currentTurn && this.$parent.currentTurn.filter(x => x.type === "ban" && x.actorCellId === this.state.localPlayerCellId && !x.completed).length > 0;

        const allActions = (<ChampSelectAction[]>[]).concat(...this.state.actions);
        const bannedChamps = allActions.filter(x => x.type === "ban" && x.completed).map(x => x.championId);
        // -1 is the "no ban" entry, which is not a champion.
        return (isCurrentlyBanning ? this.banOptions : this.pickableChampions)
            .filter(x => x > 0 && bannedChamps.indexOf(x) === -1);
    }

    /**
     * @returns the champions that can be banned. The client can answer [-1] ("no ban") for the
     * whole champ select while every champion can be banned; then it's every champion of the
     * version this queue uses (League Classic champions have 60000 added to their id).
     */
    get banOptions(): number[] {
        if (this.bannableChampions.some(x => x > 0)) return this.bannableChampions;

        const classic = this.pickableChampions.some(x => x >= 60000);
        return Object.keys(this.$parent.champions).map(Number).filter(x => x > 0 && (x >= 60000) === classic);
    }

    /**
     * @returns the header shown at the top of the prompt
     */
    get header(): string {
        if (this.mode === "dealt") return "Choose Your Champion";
        if (this.mode === "pool") return "Swap Your Champion";

        const act = this.$parent.getActions(this.state.localPlayer);
        if (!act && this.firstUncompletedPickAction) return "Declare Your Champion!";
        if (!act || act.type !== "ban") return "Pick a Champion";
        return "Ban a Champion";
    }

    /**
     * @returns the type of the finish button
     */
    get buttonType(): string {
        if (this.mode === "pool" && this.tappedEntry && this.tappedEntry.trade && this.tappedEntry.trade.state === "SENT") return "deny";
        if (this.mode !== "normal") return "confirm";

        const act = this.$parent.getActions(this.state.localPlayer);
        if (!act || act.type !== "ban") return "confirm";
        return "deny";
    }

    /**
     * @returns the text of the finish button
     */
    get buttonText(): string {
        if (this.mode === "dealt") return "Pick!";
        if (this.mode === "pool") {
            const entry = this.tappedEntry;
            if (!entry || !entry.trade) return "Swap!";
            return entry.trade.state === "SENT" ? "Cancel Trade" : "Ask to Trade";
        }

        const act = this.$parent.getActions(this.state.localPlayer);
        if (!act || act.type !== "ban") return "Pick!";
        return "Ban!";
    }

    /**
     * @returns if we can complete the current action (e.g. lock in or ban)
     */
    get canCompleteAction(): boolean {
        if (this.mode === "dealt") return this.$parent.dealtChampions.indexOf(this.tapped) !== -1;
        if (this.mode === "pool") return !!this.tappedEntry;

        const act = this.$parent.getActions(this.state.localPlayer);
        return !!(act && !act.completed && this.selectedChampion);
    }

    /**
     * @returns the id of the champion currently selected or hovered
     */
    get selectedChampion(): number {
        if (this.mode !== "normal") return this.tapped;

        const act = this.$parent.getActions(this.state.localPlayer);
        if (act) return act.championId;

        const firstUncompletedPick = this.firstUncompletedPickAction;
        if (firstUncompletedPick) return firstUncompletedPick.championId;

        return 0;
    }

    /**
     * Gets the first uncompleted pick action for the current player,
     * or undefined if there is no such action.
     */
    get firstUncompletedPickAction(): ChampSelectAction | undefined {
        const allActions: ChampSelectAction[] = Array.prototype.concat(...this.state.actions);
        return allActions.filter(x => x.type === "pick" && x.actorCellId === this.state.localPlayerCellId && !x.completed)[0];
    }

    /**
     * Selects the specified champion for the current action.
     */
    selectChampion(championId: number) {
        if (this.mode !== "normal") {
            this.tapped = championId;
            return;
        }

        const act = this.$parent.getActions(this.state.localPlayer);
        if (!act) return this.hoverChampion(championId);
        this.$root.request("/lol-champ-select/v1/session/actions/" + act.id, "PATCH", JSON.stringify({ championId }));
    }

    /**
     * Hovers the specified champion, by changing the champion of
     * the first uncompleted pick action for the current player.
     * Does nothing if there is no action to pick for.
     */
    hoverChampion(championId: number) {
        const firstUncompletedPick = this.firstUncompletedPickAction;
        if (!firstUncompletedPick) return;
        this.$root.request("/lol-champ-select/v1/session/actions/" + firstUncompletedPick.id, "PATCH", JSON.stringify({ championId }));
    }

    /**
     * Completes the current action and dismisses the picker.
     */
    completeAction() {
        if (this.mode === "dealt") return this.chooseDealt();
        if (this.mode === "pool") return this.getFromPool();

        const act = this.$parent.getActions(this.state.localPlayer)!;
        this.$root.request("/lol-champ-select/v1/session/actions/" + act.id, "PATCH", JSON.stringify({
        championId: act.championId,
        completed: true
    }));
        this.$emit("close");
    }

    /**
     * ARAM Mayhem: picks the tapped champion of the ones dealt to us. The client takes it at once.
     */
    chooseDealt() {
        const act = this.firstUncompletedPickAction;
        if (!act || !this.canCompleteAction) return;

        this.$root.request("/lol-champ-select/v1/session/actions/" + act.id, "PATCH", JSON.stringify({
            championId: this.tapped,
            completed: true
        }));
        this.tapped = 0;
        this.$emit("close");
    }

    /**
     * ARAM: swaps for the tapped bench champion, or asks the teammate who has it to trade
     * (or takes back that question).
     */
    getFromPool() {
        const entry = this.tappedEntry;
        if (!entry) return;

        if (!entry.trade) this.$parent.benchSwap(entry.championId);
        else this.$parent.swapAction("champion", entry.trade, entry.trade.state === "SENT" ? "cancel" : "request");

        this.tapped = 0;
        this.$emit("close");
    }

    /**
     * @returns the text shown instead of the grid, when there's nothing to choose from
     */
    get emptyText(): string {
        if (this.gridChampions.length) return "";
        if (this.mode === "dealt") return "Waiting for your champions...";
        if (this.mode === "pool") return "The bench is empty, and no teammate can trade with you right now.";
        return "";
    }

    /**
     * @returns the name for the specified champion
     */
    championName(id: number) {
        return this.$parent.championName(id);
    }
}
