import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import { championIcon } from "@/constants";

export interface GridEntry {
    id: number;
    name: string;
    // Shown under the name, like where an ARAM champion would come from.
    tag?: string;
}

/**
 * A searchable grid of champions, sorted by name. Emits `select` with the id of the tapped champion.
 */
@Component
export default class ChampionGrid extends Vue {
    @Prop({ default: () => [] })
    champions: GridEntry[];

    @Prop({ default: 0 })
    selected: number;

    searchTerm = "";

    mounted() {
        const input = <HTMLInputElement>this.$refs.searchInput;
        input.addEventListener("focus", () => document.body.classList.add("in-input"));
        input.addEventListener("blur", () => document.body.classList.remove("in-input"));
    }

    get shownChampions(): GridEntry[] {
        const search = this.searchTerm.toLowerCase();
        return this.champions
            .filter(x => typeof x.name === "string" && x.name.toLowerCase().includes(search))
            .sort((a, b) => a.name.localeCompare(b.name));
    }

    /**
     * League Classic champions are the same champions with 60000 added to the id.
     */
    isClassic(champion: GridEntry) {
        return champion.id >= 60000;
    }

    /**
     * @returns the tag shown under the name: the entry's own, or Classic for League Classic champions
     */
    tag(champion: GridEntry) {
        return champion.tag || (this.isClassic(champion) ? "Classic" : "");
    }

    /**
     * @returns the name without a " (Classic)" suffix, which the tag under the name shows instead
     */
    shortName(champion: GridEntry) {
        return this.isClassic(champion) ? champion.name.replace(/ \(Classic\)$/, "") : champion.name;
    }

    icon(id: number) {
        return championIcon(id);
    }
}
