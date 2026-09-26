import Vue from "vue";
import Root from "../root/root";
import { Component } from "vue-property-decorator";
import { playerName } from "@/constants";

interface Invite {
    invitationId: string;
    canAcceptInvitation: boolean;
    fromSummonerId: number;
    fromSummonerName: string;
    fromSummoner: { displayName: string, gameName?: string, profileIconId: number }; // added by us
    queueName: string; // added by us
    mapName: string; // added by us
    gameConfig: { mapId: number, queueId: number };
    state: "Pending" | "Declined";
}

@Component
export default class Invites extends Vue {
    $root: Root;

    invites: Invite[] = [];

    mounted() {
        const handleInvitationUpdate = async () => {
            const result = await this.$root.request("/lol-lobby/v2/received-invitations");

            if (result.status !== 200) {
                this.invites = [];
                return;
            }

            // Load and queue summoner info.
            const newInvites: Invite[] = result.content;
            for (const invite of newInvites) {
                invite.fromSummoner = (await this.$root.request("/lol-summoner/v1/summoners/" + invite.fromSummonerId)).content;

                const queueInfo = await this.$root.request("/lol-game-queues/v1/queues/" + invite.gameConfig.queueId);
                invite.queueName = queueInfo.content ? queueInfo.content.description : "";

                const mapInfo = await this.$root.request("/lol-maps/v1/map/" + invite.gameConfig.mapId);
                invite.mapName = mapInfo.content ? mapInfo.content.name : "";
            }


            this.invites = newInvites;
        };

        // Start observing invitation changes.
        this.$root.observe("/lol-lobby/v2/received-invitations", handleInvitationUpdate);

        // Check for initial pending invites.
        handleInvitationUpdate();
    }

    /**
     * @returns only the pending invites (the ones that can be accepted)
     */
    get pendingInvites(): Invite[] {
        return this.invites.filter(x => x.state === "Pending");
    }

    /**
     * Accepts the specified invite.
     */
    acceptInvite(invite: Invite) {
        this.$root.request("/lol-lobby/v2/received-invitations/" + invite.invitationId + "/accept", "POST");
    }

    /**
     * Declines the specified invite.
     */
    declineInvite(invite: Invite) {
        this.$root.request("/lol-lobby/v2/received-invitations/" + invite.invitationId + "/decline", "POST");
    }

    /**
     * @returns details of the invite, showing the gamemode and map.
     */
    getInviteDetails(invite: Invite): string {
        return (invite.queueName || "Unknown Queue") + " - " + (invite.mapName || "Unknown Map");
    }

    /**
     * @returns the name of the player who sent the invite
     */
    inviterName(invite: Invite): string {
        return playerName(invite.fromSummoner) || invite.fromSummonerName || "";
    }

    /**
     * @returns the path to the summoner icon for the inviter
     */
    getSummonerIcon(invite: Invite): string {
        return `https://ddragon.leagueoflegends.com/cdn/${this.$root.ddragonVersion}/img/profileicon/${invite.fromSummoner.profileIconId}.png`;
    }
}