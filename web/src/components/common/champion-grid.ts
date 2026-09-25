import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import { championIcon } from "@/constants";

export interface GridEntry {
    id: number;
    name: string;
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
            .filter(x => x.name.toLowerCase().includes(search))
            .sort((a, b) => a.name.localeCompare(b.name));
    }

    icon(id: number) {
        return championIcon(id);
    }
}
