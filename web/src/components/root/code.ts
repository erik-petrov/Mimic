/**
 * @returns the code the way Rift knows it: letters and digits only, in capitals
 */
export function normalizeCode(code: string): string {
    return code.replace(/[^0-9a-z]/gi, "").toUpperCase();
}

/**
 * @returns whether the specified code looks complete: 10 characters from this fork's Rift,
 * or 6 digits from the shared Rift
 */
export function isCompleteCode(code: string): boolean {
    const normalized = normalizeCode(code);
    return /^[0-9A-Z]{10}$/.test(normalized) || /^[0-9]{6}$/.test(normalized);
}
