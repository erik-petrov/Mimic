<template>
    <div class="champion-grid">
        <input ref="searchInput" v-model="searchTerm" type="text" class="search" autocomplete="off" spellcheck="false" placeholder="Search..." maxlength="24" autocorrect="off" autocapitalize="off">

        <div class="content">
            <a class="champion-option" v-for="champion in shownChampions" :key="champion.id">
                <img @click="$emit('select', champion.id)" :class="selected === champion.id && 'selected'" :src="icon(champion.id)">
                <div class="name">{{ champion.name }}</div>
            </a>
        </div>
    </div>
</template>

<script lang="ts" src="./champion-grid.ts"></script>

<style lang="stylus" scoped>
    .champion-grid
        flex 1
        min-height 0
        display flex
        flex-direction column

    .content
        // Take exactly the space left, so the last rows can always be scrolled into view.
        flex 1
        min-height 0
        align-content flex-start
        overflow-y scroll
        -webkit-overflow-scrolling touch
        display flex
        flex-wrap wrap
        justify-content center

    .champion-option
        display flex
        flex-direction column
        align-items center
        transition 0.2s ease

        &:active
            opacity 0.7

        .name
            color #f0e6d3
            font-size 35px
            padding 5px
            font-family LoL Body
            max-width 22vw
            white-space nowrap
            overflow hidden
            text-overflow ellipsis

    img
        box-sizing border-box
        border 2px solid transparent
        margin 15px
        width 200px
        height 200px
        opacity 0.6
        transition 0.3s ease

    img.selected
        border-color #c89c3c
        opacity 1

    .search
        box-sizing border-box
        height 110px
        padding 20px
        margin 20px 10px 20px 20px
        -webkit-appearance none
        outline none
        border-radius 0
        color #f0e6d2
        font-size 40px
        font-family "LoL Body"
        border 3px solid #785a28
        background-color black
</style>
