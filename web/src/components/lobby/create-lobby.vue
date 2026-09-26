<template>
    <div class="create-lobby">
        <div class="header">
            <a class="close"><i @click="$emit('close')" class="ion-chevron-left"></i></a>
            <span>Create Lobby</span>
        </div>

        <div class="sections">
            <div class="section" v-for="section in sections" :key="section.key" @click="selectSection(section.key)">
                <div class="icon">
                    <img :src="sectionIcon(section, 'default')">
                    <img :class="selectedSection !== section.key && 'hide'" :src="sectionIcon(section, 'active')">
                </div>
                <span class="label" :class="selectedSection === section.key && 'selected'">{{ section.title }}</span>
            </div>
        </div>

        <div class="section-title">
            {{ sectionTitle }}
        </div>

        <div class="queues" v-if="currentSection">
            <a
                class="queue"
                v-for="queue in currentSection.queues"
                :key="queue.id"
                :class="{ selected: selectedQueueId === queue.id, closed: !isOpen(queue) }"
                @click="selectQueue(queue)">
                <div class="diamond-outer">
                    <div class="diamond-inner"></div>
                </div>

                <span>{{ queueLabel(queue) }}</span>
                <span class="closed-label" v-if="!isOpen(queue)">Closed right now</span>
            </a>
        </div>

        <div class="create">
            <lcu-button @click="createLobby" :disabled="!selectedQueueId">
                Confirm
            </lcu-button>
        </div>
    </div>
</template>

<script lang="ts" src="./create-lobby.ts"></script>

<style lang="stylus">
    body.has-notch .create-lobby
        height 100vh
        padding-bottom calc(env(safe-area-inset-bottom) + 14px)

        & .header
            padding-top calc(env(safe-area-inset-top) + 40px)
</style>

<style lang="stylus" scoped>
    .create-lobby
        box-sizing border-box
        background-image url(../../static/magic-background.jpg)
        background-size cover
        background-position center
        position absolute
        top 0
        left 0
        bottom 0
        right 0
        flex 1
        transition background-image 0.3s ease // Not a standard, but most mobile browsers (chrome) support it.
        display flex
        flex-direction column
        overflow hidden

    .header
        display flex
        align-items center
        padding 30px
        background-color rgba(0, 0, 0, 0.7)
        border-bottom 1px solid white

        .close:active i
            opacity 0.7

        & i
            font-size 70px
            color #efe5d1

        & span
            margin-left 30px
            font-size 65px
            font-family LoL Display Bold
            color #f0d9a3

    .sections
        display flex
        justify-content space-around
        margin-top 30px
        padding 0 10px

        .section
            flex 1
            display flex
            flex-direction column
            align-items center
            min-width 0

        .icon
            position relative
            width 13vw
            height 13vw

        .icon img
            position absolute
            transition opacity 0.2s ease
            width 100%
            height 100%

        .icon img.hide
            opacity 0

        .label
            margin-top 8px
            font-size 24px
            line-height 1.2
            text-align center
            color #a09b8c

            &.selected
                color #f0e6d2

    .section-title
        box-sizing border-box
        margin-top 20px
        width 100%
        padding 30px
        font-family "LoL Display Bold"
        font-size 65px
        color #f0d9a3
        text-align center
        border-bottom 1px solid alpha(white, 0.7)
        text-transform uppercase

    .queues
        box-sizing border-box
        display flex
        flex-direction column
        width 100%
        padding 30px
        flex 1
        min-height 0
        overflow-y auto
        -webkit-overflow-scrolling touch

        .queue
            display flex
            align-items center
            height 75px
            padding 10px

        .queue span
            font-family "LoL Display Bold"
            font-size 50px
            margin-left 20px
            text-transform uppercase
            transition 0.2s ease
            color #bdb088

        .diamond-outer
            transform rotate(45deg)
            width 35px
            height 35px
            position relative
            background-color #87692c

        .diamond-inner
            position absolute
            top 6px
            left 6px
            width 23px
            height 23px
            transition background-color 0.2s ease
            background-color #08181f

        .queue.selected .diamond-inner
            box-sizing border-box
            border 5px solid #08181f
            background-color #f0e6d2

        .queue:active span
            color #d6c99f

        .queue.selected span
            color #efe5d1

        .queue
            flex-shrink 0
            min-height 75px
            height auto

        .queue span
            font-size 44px

        .queue.closed span
            color #5b5a56

        .queue.closed .diamond-outer
            background-color #3c3c41

        .queue .closed-label
            margin-left auto
            padding-left 20px
            font-family "LoL Body"
            font-size 28px
            text-transform none
            white-space nowrap

    .create
        align-self center
        width 100%
        display flex
        justify-content center
        padding-bottom 20px
</style>