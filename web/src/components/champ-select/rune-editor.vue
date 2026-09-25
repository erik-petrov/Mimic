<template>
    <transition enter-active-class="fadeInUp" leave-active-class="fadeOutDown">
        <div class="rune-editor" v-if="show && runes">
            <i class="ion-minus close" @click="$emit('close')"></i>
            <div class="header">Edit Rune Pages</div>

            <div class="page-options">
                <select class="league" @change="$parent.selectRunePage($event)">
                    <option :value="rune.id" :selected="rune.isActive" v-for="rune in $parent.runePages.filter(x => x.isEditable)">{{ rune.name }}</option>
                </select>

                <div class="circular-button" :class="!$parent.localChampionId && 'disabled'" @click="$parent.openRecommendations()"><i class="ion-wand"></i></div>
                <div class="circular-button" @click="addPage"><i class="ion-plus"></i></div>
                <div class="circular-button" @click="removePage"><i class="ion-trash-a"></i></div>
            </div>

            <div class="content" v-if="currentPage">
                <rune-tree-editor :page="currentPage" @change="savePage"></rune-tree-editor>
            </div>

            <div class="content" v-else>
                <!-- No viable page (one that is editable) exists. -->
                <span class="section-header">SELECT OR CREATE A PAGE</span>
            </div>
        </div>
    </transition>
</template>

<script lang="ts" src="./rune-editor.ts"></script>

<style lang="stylus">
    body.has-notch .rune-editor
        margin-top calc(env(safe-area-inset-top) + 30px)
        padding-bottom calc(env(safe-area-inset-bottom) + 20px)
</style>

<style lang="stylus" scoped>
    @import "../../common.styl"

    .fadeInUp, .fadeOutDown
        animation-duration 0.4s !important

    .rune-editor
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

        .page-options
            min-height 90px
            padding 10px 0
            display flex
            flex-direction row
            align-items center

            select
                height 90px
                flex 1

            .circular-button
                flex 0 90px
                margin 0 10px

            .circular-button.disabled
                opacity 0.4

    .content
        overflow-y scroll
        overflow-x hidden
        -webkit-overflow-scrolling touch
        padding-bottom 20px

        .section-header
            font-family "LoL Display"
            font-size 50px
            padding 20px 20px 40px 20px
            display block
            text-transform uppercase
            color #f0e6d2
            font-weight 700
            letter-spacing 0.075em
</style>