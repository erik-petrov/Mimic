import Vue from "vue";
import { Component, Prop } from "vue-property-decorator";
import { ChampSelectMember, ChampSelectState, default as ChampSelect, SwapContract, SwapKind } from "./champ-select";
import Root from "../root/root";
import { POSITION_NAMES } from "@/constants";

// A swap a teammate sent us, waiting for our answer.
interface IncomingSwap {
    kind: SwapKind;
    swap: SwapContract;
    from: ChampSelectMember;
}

@Component
export default class SwapPrompt extends Vue {
    $root: Root;
    $parent: ChampSelect;

    @Prop()
    state: ChampSelectState;

    // Id of the swap we last answered, so the buttons can't be pressed twice
    // while the client processes the answer.
    answeredId = -1;

    /**
     * @returns the first swap a teammate sent us that we haven't answered yet, if any
     */
    get incoming(): IncomingSwap | null {
        const kinds: SwapKind[] = ["position", "pickOrder", "champion"];
        for (const kind of kinds) {
            const swap = this.$parent.getSwaps(kind).filter(x => x.state === "RECEIVED")[0];
            if (!swap || swap.id === this.answeredId) continue;

            const from = this.$parent.getMember(swap.cellId);
            if (from) return { kind, swap, from };
        }

        return null;
    }

    /**
     * @returns the text describing what the incoming swap would do
     */
    get description(): string {
        const incoming = this.incoming;
        if (!incoming) return "";

        const me = this.state.localPlayer;
        const name = incoming.from.displayName;

        if (incoming.kind === "position") {
            return `${name} wants to swap lanes. You would play ${this.positionName(incoming.from)} and they would play ${this.positionName(me)}.`;
        }

        if (incoming.kind === "pickOrder") {
            return `${name} wants to swap pick order with you.`;
        }

        const theirs = this.$parent.championName(incoming.from.championId);
        const mine = this.$parent.championName(me.championId);
        return `${name} wants to trade their ${theirs} for your ${mine}.`;
    }

    /**
     * Accepts or declines the incoming swap.
     */
    answer(accept: boolean) {
        const incoming = this.incoming;
        if (!incoming) return;

        this.answeredId = incoming.swap.id;
        this.$parent.swapAction(incoming.kind, incoming.swap, accept ? "accept" : "decline");
    }

    /**
     * @returns the display name of the position assigned to the specified member
     */
    private positionName(member: ChampSelectMember): string {
        return POSITION_NAMES[member.assignedPosition.toUpperCase()] || "an unknown position";
    }
}
