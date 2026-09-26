import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import { default as ChampSelect, RunePage } from "./champ-select";
import Root from "../root/root";
import RuneTreeEditor from "../common/rune-tree-editor.vue";
import { RuneTree } from "../common/rune-tree-editor";

@Component({
    components: { runeTreeEditor: RuneTreeEditor }
})
export default class RuneEditor extends Vue {
    $root: Root;
    $parent: ChampSelect;

    @Prop()
    show: boolean;

    runes: RuneTree[] = [];

    async created() {
        this.runes = await this.$parent.loadStatic("runesReforged.json");
    }

    /**
     * @returns the page currently selected, unless there are none
     */
    get currentPage() {
        const page = this.$parent.currentRunePage;
        return page && page.isEditable ? page : undefined;
    }

    /**
     * Creates a new rune page and makes it the current selected page.
     */
    async addPage() {
        const result = await this.$root.request("/lol-perks/v1/pages", "POST", JSON.stringify({
            name: "Rune Page " + (this.$parent.runePages.length + 1),
            primaryStyleId: this.runes[0].id,
            subStyleId: this.runes[1].id,
            selectedPerkIds: [0, 0, 0, 0, 0, 0, 0, 0, 0]
        }));

        // The client refuses new pages when the page limit is reached.
        const rsp: RunePage = result.content;
        if (result.status !== 200 || !rsp || !rsp.id) {
            alert("Could not create a rune page. You may have reached your rune page limit.");
            return;
        }

        this.$parent.runePages.push(rsp);
        this.$parent.runePages.forEach(x => x.isActive = x === rsp);
        this.$root.request("/lol-perks/v1/currentpage", "PUT", "" + rsp.id);
    }

    /**
     * Saves the current page, overwriting the LCU version. This does not check if
     * there were any changes made before saving, so only call this when you are sure
     * that new changes need to be committed.
     */
    savePage() {
        if (!this.currentPage) return;

        this.$root.request("/lol-perks/v1/pages/" + this.currentPage.id, "PUT", JSON.stringify(this.currentPage));
    }

    /**
     * Deletes the current page. The LCU will automatically select a different page for us.
     */
    removePage() {
        if (!this.currentPage) return;

        this.$root.request("/lol-perks/v1/pages/" + this.currentPage.id, "DELETE");
        this.$parent.runePages = this.$parent.runePages.filter(x => x.id != this.currentPage!.id);
    }
}
