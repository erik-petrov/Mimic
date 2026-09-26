import Root, { Result } from "../root/root";
import { Role } from "@/constants";

// Conduit answers this path itself: autopick runs on the PC, not on the phone.
export const AUTOPICK_PATH = "/mimic/v1/autopick";

export const MAX_PICKS = 4;
export const MAX_BANS = 2;

export type AutopickRole = "top" | "jungle" | "middle" | "bottom" | "utility" | "any";

// The roles in the setup, with the lobby's role icons. "any" (All roles) is not a role: it fills
// in for any role without its own picks or bans, and is used in queues without roles.
export const AUTOPICK_ROLES: { key: AutopickRole, name: string, icon: Role }[] = [
    { key: "any", name: "All roles", icon: "FILL" },
    { key: "top", name: "Top", icon: "TOP" },
    { key: "jungle", name: "Jungle", icon: "JUNGLE" },
    { key: "middle", name: "Mid", icon: "MIDDLE" },
    { key: "bottom", name: "Bot", icon: "BOTTOM" },
    { key: "utility", name: "Support", icon: "UTILITY" }
];

export interface AutopickRunes {
    // recommended: the client's top recommendation. page: one of the player's pages.
    // custom: runes chosen here. none: leave the runes alone.
    type: "recommended" | "page" | "custom" | "none";
    pageId?: number;
    primaryStyleId?: number;
    subStyleId?: number;
    selectedPerkIds?: number[];
}

// A champion to pick. 0 for the skin or spells means "don't change".
export interface AutopickPick {
    championId: number;
    skinId: number;
    spell1Id: number;
    spell2Id: number;
    runes: AutopickRunes;
}

export interface AutopickRoleSetup {
    picks: AutopickPick[];
    bans: number[];
}

export type AutopickRoles = { [role: string]: AutopickRoleSetup };

export interface AutopickState {
    enabled: boolean;
    roles: AutopickRoles;
    // Seconds autopick waits into a turn before locking in, set on the server.
    lockInDelay: number;
    // What autopick is doing, shown in champ select.
    status: string;
    inChampSelect: boolean;
}

/**
 * @returns the state in the specified response, or false if Conduit doesn't know autopick
 */
export function parseAutopickState(result: Result): AutopickState | false {
    if (result.status !== 200 || !result.content || typeof result.content.roles !== "object") return false;
    return result.content;
}

export function setAutopickEnabled(root: Root, enabled: boolean) {
    return root.request(AUTOPICK_PATH + "/enabled", "PUT", JSON.stringify(enabled));
}

export function saveAutopickRoles(root: Root, roles: AutopickRoles) {
    return root.request(AUTOPICK_PATH + "/roles", "PUT", JSON.stringify(roles));
}

/**
 * @returns whether any role has something set up
 */
export function hasAutopickSetup(roles: AutopickRoles): boolean {
    return Object.keys(roles).some(key => roles[key].picks.length > 0 || roles[key].bans.length > 0);
}
