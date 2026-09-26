let ddragonPromise: Promise<string> | undefined;
export async function loadDdragon(): Promise<string> {
    if (ddragonPromise) return ddragonPromise;

    return ddragonPromise = new Promise((resolve, reject) => {
        // Load ddragon async.
        const req = new XMLHttpRequest();
        req.onreadystatechange = () => {
            if (req.readyState !== 4) return;
            if (req.status !== 200 || !req.responseText) return reject();
            const versions: string[] = JSON.parse(req.responseText);
            resolve(versions[0]);
        };
        req.open("GET", "https://ddragon.leagueoflegends.com/api/versions.json", true);
        req.send();
    });
}

const staticData: { [filename: string]: Promise<any> } = {};

/**
 * Loads the specified json file (like runesReforged.json) from the latest ddragon static data.
 * Each file is only downloaded once.
 */
export function loadStaticData(filename: string): Promise<any> {
    if (staticData[filename]) return staticData[filename];

    return staticData[filename] = loadDdragon().then(version => new Promise(resolve => {
        const req = new XMLHttpRequest();
        req.onreadystatechange = () => {
            if (req.status !== 200 || !req.responseText || req.readyState !== 4) return;
            resolve(JSON.parse(req.responseText));
        };
        req.open("GET", `https://ddragon.leagueoflegends.com/cdn/${version}/data/en_US/${filename}`, true);
        req.send();
    }));
}

const CDRAGON_GAME_DATA = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/";

/**
 * Converts a client asset path (like the iconPath of a perk) to the same file on
 * CommunityDragon, which mirrors the client's game data. The phone cannot load
 * assets from the client directly.
 */
export function gameDataAsset(path: string): string {
    if (!path) return "";
    return CDRAGON_GAME_DATA + path.replace(/^\/lol-game-data\/assets\//, "").toLowerCase();
}

/**
 * @returns the square icon for the specified champion id. Works for every champion
 * the client knows about, without needing to look up the champion's alias first.
 */
export function championIcon(id: number): string {
    return CDRAGON_GAME_DATA + "v1/champion-icons/" + id + ".png";
}

/**
 * @returns the centered splash art for the specified champion id and optional skin id
 */
export function championSplash(id: number, skinId?: number): string {
    const base = "https://cdn.communitydragon.org/latest/champion/" + id + "/splash-art/centered";
    return skinId ? base + "/skin/" + (skinId % 1000) : base;
}

/**
 * @returns the name to show for a summoner or player: their Riot ID game name,
 * or the legacy display name for data that has no Riot ID.
 */
export function playerName(player: { gameName?: string, displayName?: string } | null | undefined): string {
    if (!player) return "";
    return player.gameName || player.displayName || "";
}

export const POSITION_NAMES: { [key: string]: string } = {
    TOP: "Top",
    JUNGLE: "Jungle",
    MIDDLE: "Mid",
    BOTTOM: "Bottom",
    UTILITY: "Support",
    FILL: "Fill",
    LANE: "Lane" // nexus blitz
};


import RoleUnselected from "./static/roles/role-unselected.png";
import RoleTop from "./static/roles/role-top.png";
import RoleJungle from "./static/roles/role-jungle.png";
import RoleMid from "./static/roles/role-mid.png";
import RoleBot from "./static/roles/role-bot.png";
import RoleSupport from "./static/roles/role-support.png";
import RoleFill from "./static/roles/role-fill.png";

export type Role = "TOP" | "JUNGLE" | "MIDDLE" | "BOTTOM" | "UTILITY" | "FILL" | "UNSELECTED";

export function roleImage(role: Role) {
    if (role === "UNSELECTED") return RoleUnselected;
    if (role === "TOP") return RoleTop;
    if (role === "JUNGLE") return RoleJungle;
    if (role === "MIDDLE") return RoleMid;
    if (role === "BOTTOM") return RoleBot;
    if (role === "UTILITY") return RoleSupport;
    if (role === "FILL") return RoleFill;
    return "";
}

import HABackground from "./static/backgrounds/bg-ha.jpg";
import TTBackground from "./static/backgrounds/bg-tt.jpg";
import SRBackground from "./static/backgrounds/bg-sr.jpg";
import TFTBackground from "./static/backgrounds/bg-tft.jpg";
import MagicBackground from "./static/magic-background.jpg";

export function mapBackground(mapId: number) {
    if (!mapId) return "";
    if (mapId === 10) return "background-image: url(" + TTBackground + ");";
    if (mapId === 11) return "background-image: url(" + SRBackground + ");";
    if (mapId === 12) return "background-image: url(" + HABackground + ");";
    if (mapId === 22) return "background-image: url(" + TFTBackground + ");";
    return "background-image: url(" + MagicBackground + ");";
}
