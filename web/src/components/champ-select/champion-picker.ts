import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import { ChampSelectAction, ChampSelectState, default as ChampSelect } from "./champ-select";
import Root from "../root/root";
import ChampionGrid from "../common/champion-grid.vue";
import { GridEntry } from "../common/champion-grid";

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
        return this.selectableChampions.map(id => ({ id, name: this.championName(id) }));
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
        const act = this.$parent.getActions(this.state.localPlayer);
        if (!act && this.firstUncompletedPickAction) return "Declare Your Champion!";
        if (!act || act.type !== "ban") return "Pick a Champion";
        return "Ban a Champion";
    }

    /**
     * @returns the type of the finish button
     */
    get buttonType(): string {
        const act = this.$parent.getActions(this.state.localPlayer);
        if (!act || act.type !== "ban") return "confirm";
        return "deny";
    }

    /**
     * @returns the text of the finish button
     */
    get buttonText(): string {
        const act = this.$parent.getActions(this.state.localPlayer);
        if (!act || act.type !== "ban") return "Pick!";
        return "Ban!";
    }

    /**
     * @returns if we can complete the current action (e.g. lock in or ban)
     */
    get canCompleteAction(): boolean {
        const act = this.$parent.getActions(this.state.localPlayer);
        return !!(act && !act.completed && this.selectedChampion);
    }

    /**
     * @returns the id of the champion currently selected or hovered
     */
    get selectedChampion(): number {
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
        const act = this.$parent.getActions(this.state.localPlayer)!;
        this.$root.request("/lol-champ-select/v1/session/actions/" + act.id, "PATCH", JSON.stringify({
        championId: act.championId,
        completed: true
    }));
        this.$emit("close");
    }

    /**
     * @returns the name for the specified champion
     */
    championName(id: number) {
        return this.$parent.championName(id);
    }
}
