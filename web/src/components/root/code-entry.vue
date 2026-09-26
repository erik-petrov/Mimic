<template>
    <div class="code-entry">
        <input ref="input" type="text" :value="value" @input="changed" placeholder="Code"
               maxlength="14" autocomplete="off" autocorrect="off" autocapitalize="characters" spellcheck="false">
    </div>
</template>

<script lang="ts">
    import { Component, Prop, Vue } from "vue-property-decorator";
    import { normalizeCode } from "./code";

    @Component({})
    export default class CodeEntry extends Vue {
        @Prop()
        value: string;

        mounted() {
            const input = <HTMLInputElement>this.$refs.input;
            input.addEventListener("focus", () => document.body.classList.add("in-input"));
            input.addEventListener("blur", () => document.body.classList.remove("in-input"));
        }

        changed(ev: Event) {
            const input = <HTMLInputElement>ev.target;
            const code = normalizeCode(input.value);

            // Show the code in capitals, without anything that isn't part of it.
            if (input.value !== code) input.value = code;
            this.$emit("input", code);
        }
    }
</script>

<style lang="stylus">
    .code-entry input
        width 100%
        box-sizing border-box
        height 180px
        padding 20px
        -webkit-appearance none
        outline none
        border-radius 0
        color #f0e6d2
        font-size 90px
        letter-spacing 0.1em
        text-align center
        text-transform uppercase
        font-family "LoL Body", sans-serif
        border 3px solid #785a28
        background-color black

        &::placeholder
            color #5b5a56
            letter-spacing normal
            text-transform none
</style>
