<template>
    <transition enter-active-class="fadeInDown" leave-active-class="fadeOutUp">
        <div class="swap-prompt" v-if="incoming">
            <div class="description">{{ description }}</div>

            <div class="buttons">
                <lcu-button type="deny" @click="answer(false)">Decline</lcu-button>
                <lcu-button type="confirm" @click="answer(true)">Accept</lcu-button>
            </div>
        </div>
    </transition>
</template>

<script lang="ts" src="./swap-prompt.ts"></script>

<style lang="stylus" scoped>
    @import "../../common.styl"

    .fadeInDown, .fadeOutUp
        animation-duration 0.3s !important

    // Shown over everything else in champ select, since the request expires quickly.
    .swap-prompt
        position absolute
        top timer-status-height
        left 0
        right 0
        z-index 2
        padding 30px 20px 20px 20px
        background-color rgba(1, 10, 19, 0.95)
        border-bottom 2px solid #c89c3c
        color #f0e6d2
        font-family "LoL Body"

    .description
        font-size 40px
        text-align center
        margin-bottom 20px

    .buttons
        display flex

        button.button
            flex 1
            width auto
            height 100px
            margin 0 10px
            font-size 45px
</style>
