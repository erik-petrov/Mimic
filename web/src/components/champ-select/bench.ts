import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import { ChampSelectState, default as ChampSelect } from "./champ-select";
import Root from "../root/root";
import { championSplash } from "@/constants";

@Component
export default class Bench extends Vue {
    $root: Root;
    $parent: ChampSelect;

    @Prop()
    state: ChampSelectState;

    @Prop()
    show: boolean;

    /**
     * @returns the ids of the champions currently on the bench
     */
    get benchChampionIds(): number[] {
        return (this.state.benchChampions || []).map(x => x.championId);
    }

    /**
     * Swaps the currently selected champion with the specified champion,
     * closing the drawer.
     */
    swapWithChampion(id: number) {
        this.$root.request("/lol-champ-select/v1/session/bench/swap/" + id, "POST");
        this.$emit("close");
    }

    /**
     * @returns the background image for the specified champion
     */
    getChampionBackground(id: number) {
        return "background-image: url(" + championSplash(id) + ");";
    }

    /**
     * @returns the name of the champion with the specified id
     */
    getChampionName(id: number) {
        return this.$parent.championName(id);
    }
}