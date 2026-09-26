import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import Root from "../root/root";
import { gameDataAsset, loadStaticData } from "@/constants";

interface RuneSlot {
    runes: {
        id: number;
    }[];
}

export interface RuneTree {
    id: number;
    slots: RuneSlot[];
}

/**
 * The runes of a page: what this editor changes.
 */
export interface RuneSelection {
    primaryStyleId: number;
    subStyleId: number;
    selectedPerkIds: number[];
}

// The three rows of stat shards (offense, flex, defense), as the client lists them.
// They are not in runesReforged.json, so they are listed here.
export const STAT_ROWS = [[5008, 5005, 5007], [5008, 5010, 5001], [5011, 5013, 5001]];

const STAT_DESCRIPTIONS: { [key: number]: string } = {
    5008: "AP/AD",
    5005: "ATKSPD",
    5007: "HASTE",
    5010: "MS",
    5001: "HP/LVL",
    5011: "HP",
    5013: "TENACITY"
};

let perkIconsPromise: Promise<{ [id: number]: string }> | undefined;

/**
 * Edits the trees, runes and stat shards of a rune page. Changes the page it is given and emits
 * `change` after every change, so the owner can save it.
 */
@Component
export default class RuneTreeEditor extends Vue {
    $root: Root;

    @Prop()
    page: RuneSelection;

    runes: RuneTree[] = [];
    secondaryIndex = 0;

    // Icon paths of every perk, from the client. Used for the stat shards.
    perkIcons: { [id: number]: string } = {};

    readonly statRows = STAT_ROWS;

    async created() {
        if (!perkIconsPromise) {
            perkIconsPromise = this.$root.request("/lol-perks/v1/perks").then(result => {
                const icons: { [id: number]: string } = {};
                if (result.status !== 200 || !Array.isArray(result.content)) {
                    perkIconsPromise = undefined;
                    return icons;
                }

                result.content.forEach((x: { id: number, iconPath: string }) => icons[x.id] = x.iconPath);
                return icons;
            });
        }
        perkIconsPromise.then(icons => this.perkIcons = icons);

        this.runes = await loadStaticData("runesReforged.json");
    }

    /**
     * @returns the rune tree for the specified id
     */
    getRuneTree(id: number) {
        return this.runes.filter(x => x.id === id)[0];
    }

    /**
     * Sets the primary tree to the specified tree id. This will clear any of the current
     * selections the user has made so far. This will select the first tree that is not the
     * selected tree as the secondary tree, which will be precision most of the time.
     */
    selectPrimaryTree(id: number) {
        this.page.primaryStyleId = id;
        this.page.subStyleId = this.runes.filter(x => x.id !== id)[0].id;

        // Reset all runes except the stat shards.
        this.page.selectedPerkIds = [0, 0, 0, 0, 0, 0, this.page.selectedPerkIds[6] || 0, this.page.selectedPerkIds[7] || 0, this.page.selectedPerkIds[8] || 0];

        this.secondaryIndex = 0;
        this.changed();
    }

    /**
     * Selects the specified primary rune in the specified slot.
     */
    selectPrimaryRune(slotIndex: number, id: number) {
        this.setPerk(slotIndex, id);
        this.changed();
    }

    /**
     * Selects the specified secondary tree. This will clear all of the currently selected
     * secondary runes and check that the same tree is not selected twice.
     */
    selectSecondaryTree(id: number) {
        if (this.page.primaryStyleId === id) return;

        this.page.subStyleId = id;
        this.setPerk(4, 0);
        this.setPerk(5, 0);
        this.changed();
    }

    /**
     * Selects the specified secondary rune. This alternates so that the least
     * recently chosen secondary rune is replaced by the current choice.
     */
    selectSecondaryRune(id: number) {
        // Make sure that we are not selecting two runes from the same slot.
        const otherRune = this.page.selectedPerkIds[4 + this.secondaryIndex];
        const slot = this.getRuneTree(this.page.subStyleId).slots.filter(x => x.runes.filter(x => x.id === id).length !== 0)[0];
        if (slot.runes.filter(x => x.id === otherRune).length) return;

        this.secondaryIndex = (this.secondaryIndex + 1) % 2;
        this.setPerk(4 + this.secondaryIndex, id);
        this.changed();
    }

    /**
     * Selects the specified stat rune in the specified slot.
     */
    selectStatRune(slotIndex: number, id: number) {
        this.setPerk(6 + slotIndex, id);
        this.changed();
    }

    /**
     * Sets one rune, in a way Vue notices, so everything showing the page updates.
     */
    private setPerk(index: number, id: number) {
        this.page.selectedPerkIds.splice(index, 1, id);
    }

    private changed() {
        this.$forceUpdate();
        this.$emit("change");
    }

    /**
     * @returns the style url to the ddragon image of the specified rune or rune style
     */
    getRuneIconStyle(runeOrStyle: { icon: string }) {
        return `background-image: url(https://ddragon.leagueoflegends.com/cdn/img/${runeOrStyle.icon})`;
    }

    /**
     * @returns the style that shows the icon of the specified stat shard
     */
    getStatIconStyle(id: number) {
        const path = this.perkIcons[id];
        return path ? `background-image: url(${gameDataAsset(path)})` : "";
    }

    /**
     * @return short description of what a stat is
     */
    getStatDescription(id: number) {
        return STAT_DESCRIPTIONS[id];
    }
}
