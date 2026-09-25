import Vue from "vue";
import { Component, Prop, Watch } from "vue-property-decorator";
import { default as ChampSelect, RunePage } from "./champ-select";
import Root, { Result } from "../root/root";
import { gameDataAsset } from "@/constants";

interface RecommendedPerk {
    id: number;
    name: string;
    iconPath: string;
    slotType: string;
}

// An entry from /lol-perks/v1/recommended-pages/..., the same recommendations the client offers.
export interface RecommendedPage {
    recommendationId: string;
    position: string;
    keystone: RecommendedPerk;
    perks: RecommendedPerk[];
    primaryPerkStyleId: number;
    secondaryPerkStyleId: number;
}

@Component
export default class RuneRecommendations extends Vue {
    $root: Root;
    $parent: ChampSelect;

    @Prop()
    show: boolean;

    recommendations: RecommendedPage[] = [];
    styleNames: { [id: number]: string } = {};
    loading = false;
    error = "";

    // Recommendation being applied right now, so it can't be tapped twice.
    applying = "";

    @Watch("show")
    onShow(show: boolean) {
        if (show) this.load();
    }

    /**
     * Loads the recommended pages for our champion, position and map.
     */
    async load() {
        const champion = this.$parent.localChampionId;
        const state = this.$parent.state;
        if (!champion || !state) return;

        this.loading = true;
        this.error = "";
        this.recommendations = [];

        if (!Object.keys(this.styleNames).length) {
            const styles = await this.$root.request("/lol-perks/v1/styles");
            const names: { [id: number]: string } = {};
            if (styles.status === 200 && Array.isArray(styles.content)) {
                styles.content.forEach((x: { id: number, name: string }) => names[x.id] = x.name);
            }
            this.styleNames = names;
        }

        const position = (state.localPlayer.assignedPosition || "NONE").toUpperCase();
        const mapId = this.$parent.gameflowState ? this.$parent.gameflowState.map.id : 11;
        const result = await this.$root.request(`/lol-perks/v1/recommended-pages/champion/${champion}/position/${position}/map/${mapId}`);

        this.loading = false;
        if (result.status !== 200 || !Array.isArray(result.content)) {
            this.error = "The League client has no recommended runes for this champion" + describeError(result) + ".";
            return;
        }

        this.recommendations = result.content.filter((x: RecommendedPage) => x.keystone && Array.isArray(x.perks));
        if (!this.recommendations.length) this.error = "The League client has no recommended runes for this champion.";
    }

    /**
     * Applies the specified recommendation, then closes the list if it worked.
     */
    async choose(rec: RecommendedPage) {
        if (this.applying) return;

        this.applying = rec.recommendationId;
        try {
            const outcome = await this.apply(rec);
            this.$root.showNotification(outcome.message);
            if (outcome.ok) this.$emit("close");
        } finally {
            this.applying = "";
        }
    }

    /**
     * Writes the recommendation into a rune page and selects it. Uses the temporary recommended
     * page if there is one, else creates one. If the client has no room for a new page, asks
     * before replacing the current page. Then checks that the client really selected it.
     */
    async apply(rec: RecommendedPage): Promise<{ ok: boolean, message: string }> {
        const champion = this.$parent.localChampionId;
        const runes = {
            name: this.$parent.championName(champion) + " - " + rec.keystone.name,
            primaryStyleId: rec.primaryPerkStyleId,
            subStyleId: rec.secondaryPerkStyleId,
            selectedPerkIds: rec.perks.map(x => x.id)
        };

        let id: number;
        const temporary = this.$parent.runePages.filter(x => x.isTemporary)[0];
        if (temporary) {
            const saved = await this.$root.request("/lol-perks/v1/pages/" + temporary.id, "PUT", JSON.stringify(Object.assign({}, temporary, runes, {
                recommendationChampionId: champion,
                runeRecommendationId: rec.recommendationId
            })));
            if (saved.status >= 300) return { ok: false, message: "The League client refused the runes" + describeError(saved) + "." };
            id = temporary.id;
        } else {
            const created = await this.$root.request("/lol-perks/v1/pages", "POST", JSON.stringify(Object.assign({}, runes, {
                isTemporary: true,
                recommendationChampionId: champion,
                runeRecommendationId: rec.recommendationId,
                current: true
            })));

            if (created.status < 300 && created.content && created.content.id) {
                id = created.content.id;
            } else {
                // No room for another page. Only replace one of the user's pages if they agree.
                const current = this.$parent.currentRunePage;
                if (!current || !current.isEditable) {
                    return { ok: false, message: "No room for another rune page" + describeError(created) + ". Delete one and try again." };
                }
                if (!confirm(`There's no room for another rune page. Replace your page "${current.name}" with these runes?`)) {
                    return { ok: false, message: "Runes not changed." };
                }

                const saved = await this.$root.request("/lol-perks/v1/pages/" + current.id, "PUT", JSON.stringify(Object.assign({}, current, runes)));
                if (saved.status >= 300) return { ok: false, message: "The League client refused the runes" + describeError(saved) + "." };
                id = current.id;
            }
        }

        const selected = await this.$root.request("/lol-perks/v1/currentpage", "PUT", "" + id);
        if (selected.status >= 300) return { ok: false, message: "The League client wouldn't select the page" + describeError(selected) + "." };

        // Read back what the client has selected, instead of assuming it worked. The client
        // can take a moment to apply the change, so check a few times.
        let page: RunePage | null = null;
        for (let attempt = 0; attempt < 10; attempt++) {
            const check = await this.$root.request("/lol-perks/v1/currentpage");
            page = check.status === 200 ? check.content : null;
            if (page && page.id === id && sameRunes(page.selectedPerkIds, runes.selectedPerkIds)) {
                return { ok: true, message: "Runes set: " + page.name };
            }
            await new Promise(resolve => setTimeout(resolve, 200));
        }

        if (!page || page.id !== id) {
            return { ok: false, message: "The League client selected a different page than the one Mimic wrote." };
        }
        return { ok: false, message: "The League client didn't keep these runes on the page." };
    }

    /**
     * @returns whether the specified recommendation is the one on the selected page
     */
    isCurrent(rec: RecommendedPage): boolean {
        const page = this.$parent.currentRunePage;
        return !!page && sameRunes(page.selectedPerkIds, rec.perks.map(x => x.id)) && page.primaryStyleId === rec.primaryPerkStyleId;
    }

    /**
     * @returns the runes shown under the keystone: the other runes of both trees, without stat shards
     */
    minorPerks(rec: RecommendedPage): RecommendedPerk[] {
        return rec.perks.filter(x => x.slotType !== "kKeyStone" && x.slotType !== "kStatMod");
    }

    /**
     * @returns the names of the primary and secondary tree
     */
    treeNames(rec: RecommendedPage): string {
        const primary = this.styleNames[rec.primaryPerkStyleId] || "";
        const secondary = this.styleNames[rec.secondaryPerkStyleId] || "";
        return primary && secondary ? primary + " / " + secondary : primary || secondary;
    }

    icon(perk: RecommendedPerk): string {
        return gameDataAsset(perk.iconPath);
    }
}

function describeError(result: Result): string {
    if (result.status < 300) return "";
    const message = result.content && typeof result.content.message === "string" ? ": " + result.content.message : "";
    return " (error " + result.status + message + ")";
}

function sameRunes(a: number[] | undefined, b: number[]): boolean {
    if (!a || a.length !== b.length) return false;
    const x = a.slice().sort();
    const y = b.slice().sort();
    return x.every((id, i) => id === y[i]);
}
