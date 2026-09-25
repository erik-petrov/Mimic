import { register } from "register-service-worker"

// Called when a new version of the app has been installed and has taken over.
let onUpdate: (() => void) | null = null;
let updateReady = false;

/**
 * Registers the callback to run when a new version of the app is ready. If one
 * already arrived before this was called, the callback runs right away.
 */
export function onUpdateReady(callback: () => void) {
    onUpdate = callback;
    if (updateReady) callback();
}

if (process.env.NODE_ENV === "production") {
    register(`${process.env.BASE_URL}service-worker.js`, {
        registered(registration) {
            // Phones keep the app open in the background, so also check for a new
            // version whenever it comes back to the front.
            document.addEventListener("visibilitychange", () => {
                if (document.visibilityState === "visible") registration.update();
            });
        },
        updated() {
            updateReady = true;
            if (onUpdate) onUpdate();
        }
    });
}
