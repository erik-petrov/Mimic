<template>
    <transition enter-active-class="fadeInUp" leave-active-class="fadeOutDown">
        <div class="rune-recommendations" v-if="show">
            <i class="ion-android-close close" @click="$emit('close')"></i>
            <div class="header">Recommended Runes</div>

            <div class="content">
                <div class="message" v-if="loading">Loading recommendations...</div>
                <div class="message" v-else-if="error">{{ error }}</div>

                <div
                    v-for="rec in recommendations"
                    :key="rec.recommendationId"
                    class="recommendation"
                    :class="{ current: isCurrent(rec), applying: applying === rec.recommendationId }"
                    @click="choose(rec)">
                    <img class="keystone" :src="icon(rec.keystone)">
                    <div class="info">
                        <span class="name">{{ rec.keystone.name }}</span>
                        <span class="trees">{{ treeNames(rec) }}</span>
                        <div class="perks">
                            <img v-for="perk in minorPerks(rec)" :key="perk.id" :src="icon(perk)" :title="perk.name">
                        </div>
                    </div>
                    <span class="badge" v-if="isCurrent(rec)">Selected</span>
                </div>
            </div>
        </div>
    </transition>
</template>

<script lang="ts" src="./rune-recommendations.ts"></script>

<style lang="stylus">
    body.has-notch .rune-recommendations
        margin-top calc(env(safe-area-inset-top) + 30px)
        padding-bottom calc(env(safe-area-inset-bottom) + 20px)
</style>

<style lang="stylus" scoped>
    @import "../../common.styl"

    .fadeInUp, .fadeOutDown
        animation-duration 0.4s !important

    // Above the rune editor, which can open this list too.
    .rune-recommendations
        position absolute
        top timer-status-height
        left 0
        right 0
        bottom 0
        z-index 2
        display flex
        flex-direction column
        color #f0e6d3
        background-image url(../../static/magic-background.jpg)
        background-repeat no-repeat
        background-size cover
        font-family "LoL Body"

        .close
            position absolute
            top 22px
            right 40px
            font-size 70px

        & > .header
            width 100%
            text-align center
            border-bottom 1px solid lightgray
            margin-top 20px
            padding-bottom 20px
            font-size 60px

        .content
            flex 1
            min-height 0
            overflow-y scroll
            -webkit-overflow-scrolling touch

        .message
            padding 40px
            text-align center
            font-size 40px

    @keyframes applying
        0% { opacity: 1; }
        50% { opacity: 0.4; }
        100% { opacity: 1; }

    .recommendation
        position relative
        box-sizing border-box
        padding 25px 20px
        display flex
        align-items center
        border-bottom 1px solid alpha(#cdbe93, 0.8)

        &:active
            background-color alpha(#cdbe93, 0.15)

        &.current
            background-color alpha(#c89c3c, 0.2)

        &.applying
            animation applying 1s ease infinite

        .keystone
            width 140px
            height 140px
            margin-right 25px

        .info
            flex 1
            display flex
            flex-direction column
            min-width 0

            .name
                font-size 48px

            .trees
                font-size 32px
                color #c8aa6e
                margin 4px 0 10px 0

            .perks img
                width 64px
                height 64px
                margin-right 10px

        .badge
            align-self flex-start
            padding 4px 14px
            border 2px solid #c89c3c
            font-size 26px
            color #f0e6d2
</style>
