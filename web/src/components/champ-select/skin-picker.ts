import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import { ChampSelectState, default as ChampSelect, SkinItem } from "./champ-select";
import Root from "../root/root";
import { championSplash } from "@/constants";

@Component
export default class SkinPicker extends Vue {
    $root: Root;
    $parent: ChampSelect;

    @Prop()
    state: ChampSelectState;

    @Prop()
    show: boolean;

    /**
     * @returns the skins available for the selected champion
     */
    get availableSkins() {
        // Find ID of selected champion.
        const member = this.state.localPlayer;
        const act = this.$parent.getActions(member);
        const champId = (act ? act.championId : 0) || member.championId || member.championPickIntent;
        if (!champId) return [];

        // Also return unavailable skins, we can filter them out.
        return this.$parent.skins.filter(x => x.championId === champId);
    }

    /**
     * Selects the specified summoner, optionally swapping the spells.
     */
    selectSkin(skin: SkinItem) {
        if (!skin.ownership.owned) return;

        this.$root.request("/lol-champ-select/v1/session/my-selection", "PATCH", JSON.stringify({
            selectedSkinId: skin.id
        }));

        this.$emit("close");
    }

    /**
     * @returns the url to the splashart for the specified skin id
     */
    getSkinImage(skin: SkinItem): string {
        return `background-image: url(${championSplash(skin.championId, skin.id)});`;
    }
}