<template>
    <transition enter-active-class="fadeInUp" leave-active-class="fadeOutDown">
        <div class="autopick-setup" v-if="show">
            <i class="ion-android-arrow-back back" v-if="view !== 'roles'" @click="back()"></i>
            <i class="ion-android-close close" @click="$emit('close')"></i>
            <div class="header">{{ header }}</div>

            <!-- Every role, with what's set up for it. -->
            <div class="content" v-if="view === 'roles'">
                <div class="note">
                    Autopick runs on your PC and uses the setup for the role League gives you.
                    All roles fills in for any role without its own picks or bans, and is used in
                    queues without roles. Autopick switches itself off when the game starts.
                </div>

                <div class="row role" v-for="r in roleList" :key="r.key" @click="openRole(r.key)">
                    <img class="role-icon" :src="roleIcon(r.icon)">
                    <div class="info">
                        <span class="name">{{ r.name }}</span>
                        <div class="icons">
                            <span class="detail" v-if="isEmpty(r.key)">Nothing set up</span>
                            <img v-for="pick in roles[r.key].picks" :src="champIcon(pick.championId)">
                            <span class="ban-label" v-if="roles[r.key].bans.length">Bans</span>
                            <img class="ban" v-for="ban in roles[r.key].bans" :src="champIcon(ban)">
                        </div>
                    </div>
                    <i class="ion-chevron-right chevron"></i>
                </div>
            </div>

            <!-- One role: its picks and bans. -->
            <div class="content" v-else-if="view === 'role'">
                <span class="section-header">Picks</span>
                <div class="row pick" v-for="(pick, i) in currentRole.picks" :key="'pick' + i" @click="openPick(i)">
                    <img class="champion" :src="champIcon(pick.championId)">
                    <div class="info">
                        <span class="slot-label">{{ pickLabel(i) }}</span>
                        <span class="name">{{ championName(pick.championId) }}</span>
                        <span class="detail">{{ pickSummary(pick) }}</span>
                    </div>
                    <i class="ion-trash-a remove" @click.stop="removePick(i)"></i>
                </div>
                <div class="row add add-pick" v-if="currentRole.picks.length < maxPicks" @click="addPick()">
                    <i class="ion-plus"></i>
                    <span>{{ currentRole.picks.length ? "Add a backup champion" : "Add a champion" }}</span>
                </div>

                <span class="section-header">Bans</span>
                <div class="row ban" v-for="(ban, i) in currentRole.bans" :key="'ban' + i" @click="changeBan(i)">
                    <img class="champion" :src="champIcon(ban)">
                    <div class="info">
                        <span class="slot-label">{{ banLabel(i) }}</span>
                        <span class="name">{{ championName(ban) }}</span>
                    </div>
                    <i class="ion-trash-a remove" @click.stop="removeBan(i)"></i>
                </div>
                <div class="row add add-ban" v-if="currentRole.bans.length < maxBans" @click="addBan()">
                    <i class="ion-plus"></i>
                    <span>{{ currentRole.bans.length ? "Add a backup ban" : "Add a ban" }}</span>
                </div>

                <div class="note" v-if="role === 'any'">
                    These are used for every role that has no picks or no bans of its own.
                </div>
                <div class="note" v-else-if="!currentRole.picks.length || !currentRole.bans.length">
                    Whatever is empty here comes from All roles.
                </div>
                <div class="note">
                    Autopick skips champions that are banned, taken, or wanted by a teammate, and tries the next one.
                </div>
            </div>

            <!-- One champion to pick: skin, spells and runes. -->
            <div class="content" v-else-if="view === 'pick' && currentPick">
                <div class="row setting champion-setting" @click="changePickChampion()">
                    <img class="champion" :src="champIcon(currentPick.championId)">
                    <div class="info">
                        <span class="slot-label">Champion</span>
                        <span class="name">{{ championName(currentPick.championId) }}</span>
                    </div>
                    <i class="ion-chevron-right chevron"></i>
                </div>

                <div class="row setting skin-setting" @click="openSkins()">
                    <img class="champion" v-if="skinImage" :src="skinImage">
                    <div class="placeholder" v-else><i class="ion-tshirt"></i></div>
                    <div class="info">
                        <span class="slot-label">Skin</span>
                        <span class="name">{{ skinLabel }}</span>
                    </div>
                    <i class="ion-chevron-right chevron"></i>
                </div>

                <div class="row setting spell-setting" @click="openSpells()">
                    <div class="spell-icons" v-if="currentPick.spell1Id && currentPick.spell2Id">
                        <img :src="spellIcon(currentPick.spell1Id)">
                        <img :src="spellIcon(currentPick.spell2Id)">
                    </div>
                    <div class="placeholder" v-else><i class="ion-flash"></i></div>
                    <div class="info">
                        <span class="slot-label">Summoner spells</span>
                        <span class="name" v-if="currentPick.spell1Id && currentPick.spell2Id">{{ spellName(currentPick.spell1Id) }} + {{ spellName(currentPick.spell2Id) }}</span>
                        <span class="name" v-else>Don't change</span>
                    </div>
                    <i class="ion-chevron-right chevron"></i>
                </div>

                <span class="section-header">Runes</span>
                <div class="option runes-recommended" :class="{ selected: currentPick.runes.type === 'recommended' }" @click="chooseRunes('recommended')">
                    <span class="name">Recommended</span>
                    <span class="detail">The client's top recommendation for this champion and role</span>
                </div>
                <div class="option runes-page" v-for="page in pages" :key="page.id" :class="{ selected: currentPick.runes.type === 'page' && currentPick.runes.pageId === page.id }" @click="chooseRunes('page', page.id)">
                    <span class="name">{{ page.name }}</span>
                    <span class="detail">Your rune page</span>
                </div>
                <div class="option runes-custom" :class="{ selected: currentPick.runes.type === 'custom' }" @click="chooseRunes('custom')">
                    <span class="name">Custom</span>
                    <span class="detail">{{ customDetail }}</span>
                </div>
                <div class="option runes-none" :class="{ selected: currentPick.runes.type === 'none' }" @click="chooseRunes('none')">
                    <span class="name">Don't change</span>
                    <span class="detail">Keep whatever page is selected</span>
                </div>

                <lcu-button class="remove-pick" type="deny" @click="removePick(pickIndex)">Remove</lcu-button>
            </div>

            <template v-else-if="view === 'champion'">
                <div class="classic-toggle" :class="{ on: showClassic }" @click="toggleClassic()">
                    <div class="text">
                        <span class="label">League Classic champions</span>
                        <span class="detail">In League Classic, autopick uses the Classic version by itself</span>
                    </div>
                    <div class="switch"><div class="knob"></div></div>
                </div>
                <champion-grid :champions="gridChampions" :selected="gridSelected" @select="chooseChampion($event)"></champion-grid>
            </template>

            <div class="content" v-else-if="view === 'skin'">
                <div class="option skin-none" :class="{ selected: !currentPick.skinId }" @click="chooseSkin(0)">
                    <span class="name">Don't change</span>
                    <span class="detail">Keep the skin League picks</span>
                </div>
                <div class="message" v-if="loadingSkins">Loading skins...</div>
                <div class="row skin" v-for="skin in skins" :key="skin.id" :class="{ chroma: skin.chroma, selected: currentPick.skinId === skin.id }" @click="chooseSkin(skin.id)">
                    <img class="champion" :src="skin.image">
                    <div class="info">
                        <span class="name">{{ skin.name }}</span>
                    </div>
                </div>
            </div>

            <div class="content" v-else-if="view === 'spell'">
                <div class="option spell-none" v-if="spellSlot === 1" :class="{ selected: !currentPick.spell1Id }" @click="chooseSpell(0)">
                    <span class="name">Don't change</span>
                    <span class="detail">Keep the spells you have</span>
                </div>
                <div class="row spell" v-for="spell in availableSpells" :key="spell.id" :class="{ disabled: spellSlot === 2 && spell.id === firstSpell }" @click="chooseSpell(spell.id)">
                    <img class="champion" :src="spellIcon(spell.id)">
                    <div class="info">
                        <span class="name">{{ spell.name }}</span>
                    </div>
                </div>
            </div>

            <div class="content" v-else-if="view === 'custom-runes' && currentPick">
                <div class="note custom-status">{{ customDetail }}</div>
                <rune-tree-editor :page="currentPick.runes" @change="customRunesChanged()"></rune-tree-editor>
            </div>
        </div>
    </transition>
</template>

<script lang="ts" src="./autopick-setup.ts"></script>

<style lang="stylus">
    body.has-notch .autopick-setup
        padding-top calc(env(safe-area-inset-top) + 25px)
        padding-bottom calc(env(safe-area-inset-bottom) + 14px)

    // While searching, start below the queue bar (200px and its 3px border), which covers the top.
    body.in-queue .autopick-setup
        top 203px

    body.has-notch.in-queue .autopick-setup
        top calc(203px + env(safe-area-inset-top) + 30px)
        padding-top 0
</style>

<style lang="stylus" scoped>
    .fadeInUp, .fadeOutDown
        animation-duration 0.4s !important

    // Above the lobby's queue overlay, so the setup also works while in queue.
    .autopick-setup
        box-sizing border-box
        position absolute
        top 0
        left 0
        right 0
        bottom 0
        z-index 102
        display flex
        flex-direction column
        color #f0e6d3
        background-image url(../../static/magic-background.jpg)
        background-repeat no-repeat
        background-size cover
        font-family "LoL Body"

        .close, .back
            position absolute
            top 22px
            font-size 70px

        .close
            right 40px

        .back
            left 40px

        & > .header
            width 100%
            text-align center
            border-bottom 1px solid lightgray
            margin-top 20px
            padding 0 130px 20px 130px
            box-sizing border-box
            font-size 60px
            white-space nowrap
            overflow hidden
            text-overflow ellipsis

    .content
        flex 1
        min-height 0
        overflow-y scroll
        -webkit-overflow-scrolling touch
        padding-bottom 20px

    .section-header
        font-family "LoL Display"
        font-size 45px
        padding 30px 20px 15px 20px
        display block
        text-transform uppercase
        color #f0e6d2
        font-weight 700
        letter-spacing 0.075em

    .note, .message
        padding 25px 30px
        font-size 34px
        color #a09b8c

    .row
        box-sizing border-box
        display flex
        align-items center
        padding 20px
        border-bottom 1px solid rgba(205, 190, 147, 0.4)

        &:active
            background-color rgba(205, 190, 147, 0.15)

        &.selected
            background-color rgba(200, 156, 60, 0.2)

        &.disabled
            opacity 0.4

        &.chroma
            padding-left 80px

        .role-icon
            width 110px
            height 110px
            margin-right 25px

        .champion
            width 120px
            height 120px
            margin-right 25px
            border 2px solid #785a28

        .placeholder
            width 120px
            height 120px
            margin-right 25px
            display flex
            align-items center
            justify-content center
            font-size 60px
            color #785a28
            border 2px dashed #785a28
            box-sizing border-box

        .spell-icons
            display flex
            margin-right 25px

            img
                width 58px
                height 58px
                margin-right 4px
                border 2px solid #785a28

        .info
            flex 1
            min-width 0
            display flex
            flex-direction column

        .name
            font-size 45px
            white-space nowrap
            overflow hidden
            text-overflow ellipsis

        .slot-label
            font-size 30px
            color #c8aa6e
            text-transform uppercase
            letter-spacing 0.05em

        .detail
            font-size 30px
            color #a09b8c
            white-space nowrap
            overflow hidden
            text-overflow ellipsis

        .icons
            display flex
            align-items center
            margin-top 8px

            img
                width 64px
                height 64px
                margin-right 8px
                border 1px solid #785a28

            img.ban
                filter grayscale(100%)
                border-color #b33

            .ban-label
                font-size 28px
                color #a09b8c
                margin 0 10px

        .chevron, .remove
            font-size 55px
            padding-left 20px
            color #a09b8c

        &.add
            color #c8aa6e
            font-size 42px
            text-transform uppercase

            i
                font-size 50px
                margin 0 30px 0 40px

    .option
        display flex
        flex-direction column
        padding 22px 30px 22px 60px
        border-bottom 1px solid rgba(205, 190, 147, 0.4)
        position relative

        &:before
            content ""
            position absolute
            left 18px
            top 50%
            width 20px
            height 20px
            margin-top -12px
            border 2px solid #785a28
            transform rotate(45deg)

        &.selected:before
            background-color #c89c3c
            border-color #c89c3c

        &:active
            background-color rgba(205, 190, 147, 0.15)

        .name
            font-size 42px

        .detail
            font-size 30px
            color #a09b8c

    .remove-pick
        margin 40px 20px 20px 20px

    .classic-toggle
        display flex
        align-items center
        justify-content space-between
        margin 20px 20px 0 20px
        padding 12px 20px
        background-color rgba(30, 35, 40, 0.85)
        border 2px solid #785a28

        .text
            display flex
            flex-direction column
            min-width 0

        .label
            font-size 38px

        .detail
            font-size 28px
            color #a09b8c

        .switch
            flex-shrink 0
            position relative
            margin-left 20px
            width 110px
            height 56px
            border-radius 28px
            background-color #1e2328
            border 2px solid #785a28
            transition 0.2s ease

        .knob
            position absolute
            top 5px
            left 5px
            width 42px
            height 42px
            border-radius 50%
            background-color #a09b8c
            transition 0.2s ease

        &.on
            border-color #c89c3c

            .switch
                background-color #785a28
                border-color #c89c3c

            .knob
                left 61px
                background-color #f0e6d2
</style>
