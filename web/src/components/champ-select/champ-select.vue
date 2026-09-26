<template>
    <div class="champ-select" v-if="state && state.localPlayer" :style="background">
        <summoner-picker :state="state" :show="pickingSummonerSpell" :first="pickingFirstSummonerSpell" @close="pickingSummonerSpell = false"></summoner-picker>
        <champion-picker :state="state" :show="pickingChampion" @close="pickingChampion = false"></champion-picker>
        <skin-picker :state="state" :show="pickingSkin" @close="pickingSkin = false"></skin-picker>
        <swap-prompt :state="state"></swap-prompt>

        <timer :state="state"></timer>
        <div class="autopick-status" v-if="autopickStatus">
            <i class="ion-flash"></i>
            <span class="text">{{ autopickStatus }}</span>
            <a class="stop" v-if="$root.autopick.enabled" @click="stopAutopick()">Stop</a>
        </div>
        <members :state="state"></members>
        <player-settings
            :state="state"
            @spell="(pickingSummonerSpell = true, pickingFirstSummonerSpell = $event)"
            @expand="pickingChampion = true"
            @runes="showingRuneOverlay = true"
            @skins="pickingSkin = true">
        </player-settings>
        <rune-editor :show="showingRuneOverlay" @close="showingRuneOverlay = false"></rune-editor>
        <rune-recommendations :show="showingRecommendations" @close="showingRecommendations = false"></rune-recommendations>

    </div>
</template>

<script lang="ts" src="./champ-select.ts"></script>

<style lang="stylus">
    body.has-notch .champ-select
        height 100vh
        box-sizing border-box

        padding-top calc(env(safe-area-inset-top) + 30px)
</style>

<style lang="stylus" scoped>
    @import "../../common.styl"

    .champ-select
        z-index 10000
        position absolute
        top 0
        left 0
        bottom 0
        right 0
        background-size cover
        background-repeat no-repeat
        display flex
        flex-direction column

    .autopick-status
        display flex
        align-items center
        padding 12px 20px
        background-color rgba(1, 10, 19, 0.85)
        border-bottom 2px solid #785a28
        color #f0e6d2
        font-family "LoL Body"
        font-size 34px

        i
            color #c89c3c
            font-size 44px
            margin-right 15px

        .text
            flex 1

        .stop
            margin-left 20px
            padding 8px 24px
            border 2px solid #c8aa6e
            color #c8aa6e
            text-transform uppercase
            font-family "LoL Display"
            font-weight 700

            &:active
                opacity 0.6
</style>