<template>
    <transition enter-active-class="fadeInUp" leave-active-class="fadeOutDown">
        <div class="champion-selector" v-show="show">
            <i class="ion-minus close" @click="$emit('close')"></i>
            <div class="header">{{ header }}</div>

            <champion-grid :champions="gridChampions" :selected="selectedChampion" @select="selectChampion($event)"></champion-grid>

            <lcu-button @click="completeAction" :disabled="!canCompleteAction" :type="buttonType">
                {{ buttonText }}
            </lcu-button>
        </div>
    </transition>
</template>

<script lang="ts" src="./champion-picker.ts"></script>

<style lang="stylus">
    body.has-notch .champion-selector
        margin-top calc(env(safe-area-inset-top) + 30px)
        padding-bottom calc(env(safe-area-inset-bottom) + 20px)
</style>

<style lang="stylus" scoped>
    @import "../../common.styl"

    button.button
        margin 20px

    .fadeInUp, .fadeOutDown
        animation-duration 0.4s !important

    .champion-selector
        position absolute
        top timer-status-height
        left 0
        right 0
        bottom 0
        z-index 1
        display flex
        flex-direction column
        color #f0e6d3
        background-image url(../../static/magic-background.jpg)
        background-repeat no-repeat
        background-size cover

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
            font-family "LoL Body"
            font-size 60px

</style>