import { DatabaseSync } from "node:sqlite";
import * as crypto from "crypto";

// Where the database is stored. Set RIFT_DATABASE to keep it somewhere persistent, like a Docker volume.
export const DATABASE_PATH = process.env.RIFT_DATABASE || "database.db";

// Codes are made of these characters: digits and capital letters without the ones that are
// easy to mix up (0 and O, 1, I and L). Phones mostly get the code from the QR code, but it
// can still be typed. 10 of them make about 8 * 10^14 codes, too many to find by trying.
export const CODE_ALPHABET = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
export const CODE_LENGTH = 10;

let database: DatabaseSync | undefined;

/**
 * Creates or loads a new sqlite database.
 */
export function create() {
    database = new DatabaseSync(DATABASE_PATH);

    database.exec(`
        CREATE TABLE IF NOT EXISTS \`conduit_instances\` (
            \`code\`	    TEXT,
            \`public_key\`	TEXT,
            PRIMARY KEY(\`code\`)
        );
    `);

    // Older versions gave out 6 digit codes, which are easy to guess. Forget them: the Conduit
    // they belong to registers again and gets a new code the next time it connects.
    const old = database.prepare(`SELECT code FROM conduit_instances`).all()
        .map(x => String(x.code))
        .filter(x => !isCode(x));
    const remove = database.prepare(`DELETE FROM conduit_instances WHERE code = ?`);
    for (const code of old) remove.run(code);
    if (old.length) console.log("[+] Removed " + old.length + " old 6 digit code(s). Their Conduits get new codes when they connect.");
}

function db(): DatabaseSync {
    if (!database) throw new Error("Database not loaded yet.");
    return database;
}

/**
 * @returns whether the specified text has the form of a code this Rift gives out
 */
export function isCode(code: string): boolean {
    return code.length === CODE_LENGTH && [...code].every(x => CODE_ALPHABET.includes(x));
}

/**
 * @returns the code the way it's stored: without spaces or dashes, in capitals
 */
export function normalizeCode(code: string): string {
    return code.replace(/[\s-]/g, "").toUpperCase();
}

function randomCode(): string {
    let code = "";
    for (let i = 0; i < CODE_LENGTH; i++) code += CODE_ALPHABET[crypto.randomInt(CODE_ALPHABET.length)];
    return code;
}

/**
 * Generates a new unique code for the specified public key and returns that key.
 * Either inserts the public key in the database, or returns the existing code
 * if it already existed.
 */
export function generateCode(pubkey: string): string {
    const existing = db().prepare(`SELECT code FROM conduit_instances WHERE public_key = ? LIMIT 1`).get(pubkey);
    if (existing && isCode(String(existing.code))) return String(existing.code);

    let code: string;
    while (true) {
        code = randomCode();

        // Check if it already existed. Break if unique, else loop again.
        if (!lookup(code)) break;
    }

    // Replace the old code of this Conduit, if it had one.
    db().prepare(`DELETE FROM conduit_instances WHERE public_key = ?`).run(pubkey);
    db().prepare(`INSERT INTO conduit_instances VALUES (?, ?)`).run(code, pubkey);
    return code;
}

/**
 * Looks up the public key belonging to the specified code. Returns either the
 * key, or null if not found.
 */
export function lookup(code: string): { public_key: string, code: string } | null {
    if (typeof code !== "string") return null;

    code = normalizeCode(code);
    if (!isCode(code)) return null;

    const entry = db().prepare(`SELECT code, public_key FROM conduit_instances WHERE code = ? LIMIT 1`).get(code);
    return entry ? { code: String(entry.code), public_key: String(entry.public_key) } : null;
}

/**
 * Checks if the specified code is still a valid entry. If yes, updates the pubkey for
 * said code and returns true. Else, returns false.
 */
export function potentiallyUpdate(code: string, pubkey: string): boolean {
    if (!lookup(code)) return false;

    db().prepare(`UPDATE conduit_instances SET public_key = ? WHERE code = ?`).run(pubkey, code);
    return true;
}
