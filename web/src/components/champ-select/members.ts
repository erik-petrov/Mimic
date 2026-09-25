import Root from "../root/root";
import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import { default as ChampSelect, ChampSelectState, ChampSelectMember, SwapContract, SwapKind } from "./champ-select";
import { championSplash, POSITION_NAMES } from "@/constants";

const SWAP_LABELS: { [kind in SwapKind]: string } = {
    position: "Swap lane",
    pickOrder: "Swap order",
    champion: "Trade"
};

// A swap that can be requested, or cancelled if we already sent it.
interface SwapOption {
    kind: SwapKind;
    swap: SwapContract;
    label: string;
}

@Component
export default class Members extends Vue {
    $root: Root;
    $parent: ChampSelect;

    @Prop()
    state: ChampSelectState;

    /**
     * @returns the background champion splash image for the specified member.
     */
    getBackgroundStyle(member: ChampSelectMember): string {
        const act = this.$parent.getActions(member);
        const champId = (act ? act.championId : 0) || member.championId || member.championPickIntent;
        if (!champId) return "background-color: transparent;";

        const fade = champId === member.championPickIntent ? "opacity: 0.6;" : "";

        // Show skins if everyone has picked, else just show the champs.
        const skinId = this.$parent.hasEveryonePicked ? member.selectedSkinId : 0;
        return `background-image: url(${championSplash(champId, skinId)}); ${fade}`;
    }

    /**
     * @returns the active overlay animation class for the specified member.
     */
    getActiveOverlayClass(member: ChampSelectMember): string {
        if (!this.state) return "";
        if (this.state.timer.phase !== "BAN_PICK") return "";
        const act = this.$parent.getActions(member);
        if (!act || act.completed) return "";
        return act.type === "ban" ? "banning" : "picking";
    }

    /**
     * @returns the subtext for the specified member.
     */
    getMemberSubtext(member: ChampSelectMember): string {
        if (!this.state) return "";
        let extra = this.state.timer.phase === "PLANNING" && member === this.state.localPlayer ? "Declaring Intent" : "";

        const cur = this.$parent.getActions(member);
        if (cur && !cur.completed && !extra) {
            extra = cur.type === "ban" ? "Banning..." : "Picking...";
        }

        const next = this.$parent.getActions(member, true);
        if (next && !extra) {
            extra = next.type === "ban" ? "Banning Next..." : "Picking Next...";
        }

        if (!member.assignedPosition) return extra;
        return POSITION_NAMES[member.assignedPosition.toUpperCase()] + (extra ? " - " + extra : "");
    }

    /**
     * @returns the swaps we can request with the specified teammate, or cancel if already sent.
     * Requests from the teammate are answered in the swap prompt instead.
     */
    getSwapOptions(member: ChampSelectMember): SwapOption[] {
        if (!member.isFriendly || member.playerType === "BOT" || member.cellId === this.state.localPlayerCellId) return [];

        const kinds: SwapKind[] = ["position", "pickOrder", "champion"];
        const options: SwapOption[] = [];
        for (const kind of kinds) {
            const swap = this.$parent.getSwap(kind, member.cellId);
            if (!swap) continue;

            if (swap.state === "AVAILABLE") options.push({ kind, swap, label: SWAP_LABELS[kind] });
            if (swap.state === "SENT") options.push({ kind, swap, label: "Cancel" });
        }
        return options;
    }

    /**
     * Requests the specified swap, or cancels it if we already sent it.
     */
    toggleSwap(option: SwapOption) {
        this.$parent.swapAction(option.kind, option.swap, option.swap.state === "SENT" ? "cancel" : "request");
    }

    /**
     * @returns the url to the icon for the specified summoner icon id
     */
    getSummonerSpellImage(id: number): string {
        if (!this.$parent.summonerSpellDetails[id]) return "";

        return `https://ddragon.leagueoflegends.com/cdn/${this.$root.ddragonVersion}/img/spell/${this.$parent.summonerSpellDetails[id].id}.png`;
    }
}
