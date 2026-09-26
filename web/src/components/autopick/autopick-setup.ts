import Vue from "vue";
import { Component, Prop, Watch } from "vue-property-decorator";
import Root, { Result } from "../root/root";
import ChampionGrid from "../common/champion-grid.vue";
import { GridEntry } from "../common/champion-grid";
import RuneTreeEditor from "../common/rune-tree-editor.vue";
import { championIcon, gameDataAsset, Role, roleImage } from "@/constants";
import {
    AUTOPICK_ROLES,
    AutopickPick,
    AutopickRole,
    AutopickRoles,
    AutopickRunes,
    MAX_BANS,
    MAX_PICKS,
    saveAutopickRoles
} from "./autopick-state";

interface ChampionSummary {
    id: number;
    name: string;
}

interface OwnedChampion {
    id: number;
    name: string;
    freeToPlay: boolean;
    ownership: { owned: boolean, rental: { rented: boolean } };
}

interface Ownership {
    ownership: { owned: boolean };
}

interface InventorySkin extends Ownership {
    id: number;
    name: string;
    isBase: boolean;
    tilePath: string;
    chromas: (Ownership & { id: number, name: string, chromaPath: string })[];
}

interface SkinOption {
    id: number;
    name: string;
    image: string;
    chroma: boolean;
}

interface Spell {
    id: number;
    name: string;
    iconPath: string;
    gameModes: string[];
}

interface RunePageSummary {
    id: number;
    name: string;
    isTemporary: boolean;
}

type View = "roles" | "role" | "pick" | "champion" | "skin" | "spell" | "custom-runes";

// League Classic champions are the same champions with this added to their id. Only League Classic
// offers them; autopick uses the Classic version there by itself.
const CLASSIC_OFFSET = 60000;
const SHOW_CLASSIC_KEY = "autopick-show-classic";

// Where a new custom rune page starts: Precision and Domination, with the usual stat shards.
const NEW_CUSTOM_RUNES = { primaryStyleId: 8000, subStyleId: 8100, selectedPerkIds: [0, 0, 0, 0, 0, 0, 5008, 5008, 5001] };

/**
 * The autopick setup: a list of roles, and for each role the champions to pick (with skin,
 * summoner spells and runes) and the champions to ban. Every change is sent to Conduit right away.
 */
@Component({
    components: {
        championGrid: ChampionGrid,
        runeTreeEditor: RuneTreeEditor
    }
})
export default class AutopickSetup extends Vue {
    $root: Root;

    @Prop()
    show: boolean;

    view: View = "roles";
    role: AutopickRole = "top";
    pickIndex = 0;
    banIndex = 0;

    // What the champion grid chooses: a champion to pick or one to ban.
    choosing: "pick" | "ban" = "pick";

    // Which summoner spell is being chosen, and the first one while choosing the second.
    spellSlot = 1;
    firstSpell = 0;

    roles: AutopickRoles = {};

    champions: ChampionSummary[] = [];
    pickable: number[] = [];
    spells: Spell[] = [];
    pages: RunePageSummary[] = [];
    summonerId = 0;

    // Whether the champion grid also lists League Classic champions. Remembered on this phone.
    showClassic = readShowClassic();

    skins: SkinOption[] = [];
    skinsFor = 0;
    loadingSkins = false;

    readonly roleList = AUTOPICK_ROLES;
    readonly maxPicks = MAX_PICKS;
    readonly maxBans = MAX_BANS;

    @Watch("show", { immediate: true })
    onShow(show: boolean) {
        if (!show) return;

        // Work on a copy of Conduit's setup; every change is sent back.
        const state = this.$root.autopick;
        const roles: AutopickRoles = JSON.parse(JSON.stringify(state ? state.roles : {}));
        AUTOPICK_ROLES.forEach(x => {
            if (!roles[x.key]) roles[x.key] = { picks: [], bans: [] };
        });
        this.roles = roles;
        this.view = "roles";
        this.load();
    }

    /**
     * Loads the champions, spells and rune pages to choose from.
     */
    async load() {
        const [summary, owned, spells, pages, summoner] = await Promise.all([
            this.$root.request("/lol-game-data/assets/v1/champion-summary.json"),
            this.$root.request("/lol-champions/v1/owned-champions-minimal"),
            this.$root.request("/lol-game-data/assets/v1/summoner-spells.json"),
            this.$root.request("/lol-perks/v1/pages"),
            this.$root.request("/lol-summoner/v1/current-summoner")
        ]);

        // Every champion, for bans and names. Both lists have names; either one may be missing.
        const names: { [id: number]: string } = {};
        [summary, owned].filter(isList).forEach(result => result.content.forEach((x: ChampionSummary) => {
            if (x && x.id > 0 && typeof x.name === "string" && x.name) names[x.id] = x.name;
        }));
        this.champions = Object.keys(names).map(id => ({ id: +id, name: names[+id] + (+id >= CLASSIC_OFFSET ? " (Classic)" : "") }));
        if (!this.champions.length) {
            this.$root.showNotification("Could not load the champion list (errors " + summary.status + " and " + owned.status + ").");
        }

        if (isList(owned)) {
            this.pickable = owned.content
                .filter((x: OwnedChampion) => x.ownership && (x.ownership.owned || (x.ownership.rental && x.ownership.rental.rented)) || x.freeToPlay)
                .map((x: OwnedChampion) => x.id);
        }
        if (isList(spells)) this.spells = spells.content;
        if (isList(pages)) this.pages = pages.content.filter((x: RunePageSummary) => !x.isTemporary);
        if (summoner.status === 200 && summoner.content) this.summonerId = summoner.content.summonerId;
    }

    get header(): string {
        if (this.view === "roles") return "Setup Autopick";
        if (this.view === "role") return this.roleName;
        if (this.view === "pick") return this.currentPick ? this.championName(this.currentPick.championId) : "";
        if (this.view === "champion") return this.choosing === "ban" ? "Choose a Ban" : "Choose a Champion";
        if (this.view === "skin") return "Choose a Skin";
        if (this.view === "spell") return this.spellSlot === 1 ? "First Summoner Spell" : "Second Summoner Spell";
        return "Custom Runes";
    }

    get roleName(): string {
        return AUTOPICK_ROLES.filter(x => x.key === this.role)[0].name;
    }

    get currentRole() {
        return this.roles[this.role];
    }

    get currentPick(): AutopickPick | undefined {
        return this.currentRole ? this.currentRole.picks[this.pickIndex] : undefined;
    }

    /**
     * @returns the champions the grid shows: every champion for bans, the ones we can play for picks
     */
    get gridChampions(): GridEntry[] {
        const list = this.choosing === "ban" || !this.pickable.length
            ? this.champions
            : this.pickable.map(id => ({ id, name: this.championName(id) }));
        return this.showClassic ? list : list.filter(x => x.id < CLASSIC_OFFSET);
    }

    toggleClassic() {
        this.showClassic = !this.showClassic;
        try {
            localStorage.setItem(SHOW_CLASSIC_KEY, this.showClassic ? "1" : "0");
        } catch (e) {
            // Private mode: the toggle just isn't remembered.
        }
    }

    get gridSelected(): number {
        if (this.choosing === "ban") return this.currentRole.bans[this.banIndex] || 0;
        return this.currentPick ? this.currentPick.championId : 0;
    }

    /**
     * @returns the spells for the current role: Summoner's Rift ones, plus ARAM ones for All roles
     */
    get availableSpells(): Spell[] {
        const modes = this.role === "any" ? ["CLASSIC", "ARAM"] : ["CLASSIC"];
        return this.spells.filter(x => x.gameModes && x.gameModes.some(mode => modes.indexOf(mode) !== -1));
    }

    back() {
        if (this.view === "roles") this.$emit("close");
        else if (this.view === "role") this.view = "roles";
        else if (this.view === "champion" && this.choosing === "pick" && this.currentPick) this.view = "pick";
        else if (this.view === "champion" || this.view === "pick") this.view = "role";
        else this.view = "pick";
    }

    openRole(role: AutopickRole) {
        this.role = role;
        this.view = "role";
    }

    addPick() {
        this.choosing = "pick";
        this.pickIndex = this.currentRole.picks.length;
        this.view = "champion";
    }

    openPick(index: number) {
        this.pickIndex = index;
        this.view = "pick";
        this.loadSkins();
    }

    changePickChampion() {
        this.choosing = "pick";
        this.view = "champion";
    }

    removePick(index: number) {
        this.currentRole.picks.splice(index, 1);
        this.save();
        this.view = "role";
    }

    addBan() {
        this.choosing = "ban";
        this.banIndex = this.currentRole.bans.length;
        this.view = "champion";
    }

    changeBan(index: number) {
        this.choosing = "ban";
        this.banIndex = index;
        this.view = "champion";
    }

    removeBan(index: number) {
        this.currentRole.bans.splice(index, 1);
        this.save();
    }

    /**
     * Handles a champion tapped in the grid.
     */
    chooseChampion(id: number) {
        if (this.choosing === "ban") {
            this.currentRole.bans.splice(this.banIndex, 1, id);
            this.save();
            this.view = "role";
            return;
        }

        const pick = this.currentPick;
        if (pick) {
            // A skin belongs to one champion, so a new champion starts without one.
            if (pick.championId !== id) pick.skinId = 0;
            pick.championId = id;
        } else {
            this.currentRole.picks.push({ championId: id, skinId: 0, spell1Id: 0, spell2Id: 0, runes: { type: "recommended" } });
        }

        this.save();
        this.openPick(this.pickIndex);
    }

    openSkins() {
        this.view = "skin";
        this.loadSkins();
    }

    /**
     * Loads the skins we own for the champion of the current pick.
     */
    async loadSkins() {
        const pick = this.currentPick;
        if (!pick || this.skinsFor === pick.championId || !this.summonerId) return;

        this.skins = [];
        this.skinsFor = pick.championId;
        this.loadingSkins = true;

        const result = await this.$root.request(`/lol-champions/v1/inventories/${this.summonerId}/champions/${pick.championId}/skins`);
        this.loadingSkins = false;
        if (!isList(result) || this.skinsFor !== pick.championId) return;

        const options: SkinOption[] = [];
        result.content.filter((x: InventorySkin) => x.ownership && x.ownership.owned).forEach((skin: InventorySkin) => {
            options.push({ id: skin.id, name: skin.isBase ? "Default" : skin.name, image: gameDataAsset(skin.tilePath), chroma: false });
            (skin.chromas || []).filter(x => x.ownership && x.ownership.owned).forEach(chroma => {
                options.push({ id: chroma.id, name: chroma.name, image: gameDataAsset(chroma.chromaPath), chroma: true });
            });
        });
        this.skins = options;
    }

    chooseSkin(id: number) {
        this.currentPick!.skinId = id;
        this.save();
        this.view = "pick";
    }

    get skinLabel(): string {
        const pick = this.currentPick;
        if (!pick || !pick.skinId) return "Don't change";
        const skin = this.skins.filter(x => x.id === pick.skinId)[0];
        return skin ? skin.name : "Skin " + pick.skinId;
    }

    get skinImage(): string {
        const pick = this.currentPick;
        const skin = pick && this.skins.filter(x => x.id === pick.skinId)[0];
        return skin ? skin.image : "";
    }

    openSpells() {
        this.spellSlot = 1;
        this.firstSpell = 0;
        this.view = "spell";
    }

    /**
     * Handles a tapped summoner spell. The first tap chooses the first spell, the second tap the
     * second one. "Don't change" (0) clears both.
     */
    chooseSpell(id: number) {
        const pick = this.currentPick!;

        if (id === 0) {
            pick.spell1Id = 0;
            pick.spell2Id = 0;
        } else if (this.spellSlot === 1) {
            this.firstSpell = id;
            this.spellSlot = 2;
            return;
        } else {
            if (id === this.firstSpell) return;
            pick.spell1Id = this.firstSpell;
            pick.spell2Id = id;
        }

        this.save();
        this.view = "pick";
    }

    chooseRunes(type: AutopickRunes["type"], pageId?: number) {
        const pick = this.currentPick!;

        if (type === "custom") {
            if (pick.runes.type !== "custom") pick.runes = Object.assign({ type: "custom" }, JSON.parse(JSON.stringify(NEW_CUSTOM_RUNES)));
            this.save();
            this.view = "custom-runes";
            return;
        }

        pick.runes = type === "page" ? { type, pageId } : { type };
        this.save();
    }

    customRunesChanged() {
        this.$forceUpdate();
        this.save();
    }

    /**
     * @returns how complete the custom runes are
     */
    get customDetail(): string {
        const pick = this.currentPick;
        if (!pick || pick.runes.type !== "custom") return "Choose every rune yourself";

        const missing = (pick.runes.selectedPerkIds || []).filter(x => !x).length;
        if (!missing) return "All runes chosen";
        return missing === 1 ? "1 rune still to choose" : missing + " runes still to choose";
    }

    /**
     * Sends the whole setup to Conduit.
     */
    async save() {
        const result = await saveAutopickRoles(this.$root, this.roles);
        if (result.status !== 200) {
            const message = result.content && result.content.message ? ": " + result.content.message : "";
            this.$root.showNotification("Could not save the autopick setup (error " + result.status + message + ").");
        }
    }

    pickLabel(index: number): string {
        return index === 0 ? "First choice" : "Backup " + index;
    }

    banLabel(index: number): string {
        return index === 0 ? "Ban" : "Backup ban";
    }

    /**
     * @returns a short description of a pick's spells and runes, for the role screen
     */
    pickSummary(pick: AutopickPick): string {
        const parts: string[] = [];
        if (pick.spell1Id && pick.spell2Id) parts.push(this.spellName(pick.spell1Id) + " + " + this.spellName(pick.spell2Id));
        if (pick.skinId) parts.push("skin set");
        parts.push(this.runesLabel(pick.runes));
        return parts.join(", ");
    }

    runesLabel(runes: AutopickRunes): string {
        if (runes.type === "none") return "runes unchanged";
        if (runes.type === "custom") return "custom runes";
        if (runes.type === "page") {
            const page = this.pages.filter(x => x.id === runes.pageId)[0];
            return page ? "runes: " + page.name : "a rune page that no longer exists";
        }
        return "recommended runes";
    }

    isEmpty(role: AutopickRole): boolean {
        const setup = this.roles[role];
        return !setup || (!setup.picks.length && !setup.bans.length);
    }

    championName(id: number): string {
        const champion = this.champions.filter(x => x.id === id)[0];
        return champion ? champion.name : "Champion " + id;
    }

    spellName(id: number): string {
        const spell = this.spells.filter(x => x.id === id)[0];
        return spell ? spell.name : "Spell " + id;
    }

    spellIcon(id: number): string {
        const spell = this.spells.filter(x => x.id === id)[0];
        return spell ? gameDataAsset(spell.iconPath) : "";
    }

    champIcon(id: number): string {
        return championIcon(id);
    }

    roleIcon(role: Role): string {
        return roleImage(role);
    }
}

function readShowClassic(): boolean {
    try {
        return localStorage.getItem(SHOW_CLASSIC_KEY) === "1";
    } catch (e) {
        return false;
    }
}

function isList(result: Result): boolean {
    return result.status === 200 && Array.isArray(result.content);
}
