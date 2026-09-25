import Vue from "vue";
import Root from "../root/root";
import { Component, Prop } from "vue-property-decorator";
import { InvitationMetadata, LobbyState } from "./lobby";
import { playerName } from "@/constants";

interface InvitationSuggestion {
    summonerId: number;
    summonerName: string;
}

/**
 * Simple role picker. Pressing the X emits the 'selected' event with the same roles.
 */
@Component
export default class InviteOverlay extends Vue {
    $root: Root;

    @Prop()
    show: boolean;

    @Prop()
    state: LobbyState;

    inviteName: string = "";
    suggestions: InvitationSuggestion[] = [];

    mounted() {
        this.$root.observe("/lol-suggested-players/v1/suggested-players", result => {
            this.suggestions = result.status === 200 ? result.content : [];
        });

        (<any>this.$refs.inviteField).addEventListener("focus", () => {
            document.body.classList.add("in-input");
        });

        (<any>this.$refs.inviteField).addEventListener("blur", () => {
            document.body.classList.remove("in-input");
        });
    }

    destroyed() {
        this.$root.unobserve("/lol-suggested-players/v1/suggested-players");
    }

    /**
     * Invites the summoner with the specified id.
     */
    invite(toSummonerId: number) {
        this.$root.request("/lol-lobby/v2/lobby/invitations", "POST", JSON.stringify([{ toSummonerId }]));
    }

    /**
     * Looks up the Riot ID the user entered (Name#TAG), inviting the player if they exist.
     * Summoner names were replaced by Riot IDs, so the tag is required.
     */
    async inviteManually() {
        const riotId = this.inviteName.trim();
        const hash = riotId.lastIndexOf("#");
        if (hash <= 0 || hash === riotId.length - 1) {
            alert("Enter the full Riot ID, including the tag. For example: Name#EUW");
            return;
        }

        const gameName = riotId.substring(0, hash);
        const tagLine = riotId.substring(hash + 1);
        const lookup = await this.$root.request("/lol-summoner/v1/alias/lookup?gameName=" + encodeURIComponent(gameName) + "&tagLine=" + encodeURIComponent(tagLine));
        const puuid: string | undefined = lookup.status === 200 && lookup.content ? lookup.content.puuid : undefined;
        if (!puuid) {
            alert(riotId + " was not found. Did you spell the name and tag properly?");
            return;
        }

        // Invite by both ids, since the summoner id is what the rest of the lobby uses.
        const summoner = await this.$root.request("/lol-summoner/v2/summoners/puuid/" + encodeURIComponent(puuid));
        const invitation: { toPuuid: string, toSummonerId?: number } = { toPuuid: puuid };
        if (summoner.status === 200 && summoner.content && summoner.content.summonerId) {
            invitation.toSummonerId = summoner.content.summonerId;
        }

        this.$root.request("/lol-lobby/v2/lobby/invitations", "POST", JSON.stringify([invitation]));
        this.inviteName = ""; // clear field
    }

    /**
     * @returns the name of the player invited by the specified invitation
     */
    invitedName(invite: InvitationMetadata): string {
        return playerName(invite.toSummoner) || invite.toSummonerName || "";
    }

    /**
     * Returns an icon name that shows the state of the specified invite.
     */
    getInvitationIcon(invite: InvitationMetadata): string {
        if (invite.state === "Declined") return "ion-close";
        if (invite.state === "Accepted") return "ion-checkmark";
        if (invite.state === "Kicked") return "ion-close"; // maybe find a better icon for this?
        return "ion-ios-more";
    }
}