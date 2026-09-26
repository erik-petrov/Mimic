import Vue from "vue";
import Component from "vue-class-component";
import Root from "../root/root";

/**
 * Represents a game queue. Only the properties used here.
 */
interface GameQueue {
    id: number;
    mapId: number;
    gameMode: string;
    name?: string;
    description: string;
    category: string;
    queueAvailability: string;
    isVisible?: boolean;
    gameSelectModeGroup?: string;
    gameSelectCategory?: string;
    gameSelectPriority?: number;
}

/**
 * A group of queues, shown as one icon, like the client's game select.
 */
interface Section {
    key: string;
    title: string;
    icon: string;
    queues: GameQueue[];
}

// The client's mode groups, in the order the client shows them, with their titles and map icons.
// Co-op vs. AI queues get their own section. Unknown groups go last, with the rotating mode icon.
const SECTIONS: { key: string, title: string, icon: string }[] = [
    { key: "kSummonersRift", title: "Summoner's Rift", icon: "sr" },
    { key: "kARAM", title: "ARAM", icon: "ha" },
    { key: "kJade", title: "League Classic", icon: "rgm" },
    { key: "kAlternativeLeagueGameModes", title: "Special Modes", icon: "rgm" },
    { key: "kTeamfightTactics", title: "Teamfight Tactics", icon: "tft" },
    { key: "bots", title: "Co-op vs. AI", icon: "sr" }
];

/**
 * Lists every queue the client shows in its game select: visible PvP and Co-op vs. AI queues,
 * grouped and ordered the way the client does it. Queues that are visible but switched off (like
 * Ranked 5v5 outside its hours) are listed as closed and can't be chosen.
 */
@Component({ })
export default class CreateLobby extends Vue {
    $root: Root;

    iconPaths: { [key: string]: string } = {};
    queues: GameQueue[] = [];

    selectedSection = "";
    selectedQueueId = 0;

    created() {
        // Prepare icon paths.
        // Note that even though promises are used, these all resolve synchronously.
        for (const map of ["sr", "ha", "tt", "tft", "rgm"]) {
            import(/* webpackMode: "eager" */ `../../static/maps/${map}-default.png`).then(result => {
                this.iconPaths[map + "-default"] = result.default;
            });

            import(/* webpackMode: "eager" */ `../../static/maps/${map}-active.png`).then(result => {
                this.iconPaths[map + "-active"] = result.default;
            });
        }
    }

    mounted() {
        this.$root.observe("/lol-game-queues/v1/queues", data => {
            this.queues = data.status === 200 && Array.isArray(data.content) ? data.content : [];

            // Keep the choice if it is still there, else start at the first open queue.
            const section = this.sections.filter(x => x.key === this.selectedSection)[0];
            if (!section || !section.queues.some(x => x.id === this.selectedQueueId && isOpen(x))) {
                const first = this.sections.filter(x => x.queues.some(isOpen))[0] || this.sections[0];
                if (first) this.selectSection(first.key);
                else {
                    this.selectedSection = "";
                    this.selectedQueueId = 0;
                }
            }
        });
    }

    destroyed() {
        this.$root.unobserve("/lol-game-queues/v1/queues");
    }

    /**
     * @returns the sections with their queues, highest priority first like the client
     */
    get sections(): Section[] {
        const shown = this.queues.filter(x => x.isVisible !== false && (x.category === "PvP" || x.category === "VersusAi"));
        const sectionOf = (queue: GameQueue) => {
            if (queue.category === "VersusAi" || queue.gameSelectCategory === "kVersusAI") return "bots";
            return queue.gameSelectModeGroup || "kAlternativeLeagueGameModes";
        };

        const keys = SECTIONS.map(x => x.key);
        shown.forEach(x => {
            if (keys.indexOf(sectionOf(x)) === -1) keys.push(sectionOf(x));
        });

        return keys.map(key => {
            const known = SECTIONS.filter(x => x.key === key)[0];
            return {
                key,
                title: known ? known.title : "Other Modes",
                icon: known ? known.icon : "rgm",
                queues: shown
                    .filter(x => sectionOf(x) === key)
                    .sort((a, b) => (b.gameSelectPriority || 0) - (a.gameSelectPriority || 0) || a.id - b.id)
            };
        }).filter(x => x.queues.length > 0);
    }

    get currentSection(): Section | undefined {
        return this.sections.filter(x => x.key === this.selectedSection)[0];
    }

    /**
     * Selects the specified section and its first open queue.
     */
    selectSection(key: string) {
        this.selectedSection = key;
        const section = this.currentSection;
        const open = section ? section.queues.filter(isOpen) : [];
        this.selectedQueueId = open.length ? open[0].id : 0;
    }

    selectQueue(queue: GameQueue) {
        if (isOpen(queue)) this.selectedQueueId = queue.id;
    }

    /**
     * @returns what to call a queue. Its description tells queues of one mode apart ("Draft Pick",
     * "Ranked Flex"), but special modes need their name: Nexus Blitz's description is "Blind Pick".
     */
    queueLabel(queue: GameQueue) {
        if (this.selectedSection === "kAlternativeLeagueGameModes" && queue.name) return queue.name;
        return queue.description || queue.name || "Queue " + queue.id;
    }

    isOpen(queue: GameQueue) {
        return isOpen(queue);
    }

    /**
     * @returns the url to the map icon for the specified section
     */
    sectionIcon(section: Section, extra: string) {
        return this.iconPaths[section.icon + "-" + extra];
    }

    /**
     * Creates a lobby with the currently chosen queue.
     */
    createLobby() {
        if (!this.selectedQueueId) return;
        this.$root.request("/lol-lobby/v2/lobby", "POST", JSON.stringify({
            queueId: this.selectedQueueId
        }));
    }

    get sectionTitle() {
        return this.currentSection ? this.currentSection.title : "";
    }
}

/**
 * @returns whether the queue can be joined right now
 */
function isOpen(queue: GameQueue): boolean {
    return queue.queueAvailability === "Available";
}
