> [!CAUTION]
> This open source version of Mimic is no longer maintained. Thanks for all the positive feedback over the years!

![Mimic Logo](assets/mimic-logo.png?raw=true)

[![Build Status](https://travis-ci.org/molenzwiebel/Mimic.svg?branch=master)](https://travis-ci.org/molenzwiebel/Mimic)
[![Discord](https://discordapp.com/api/guilds/249481856687407104/widget.png?style=shield)](https://discord.gg/bfxdsRC)

# :satellite: Mimic
The new League client. Except it's on your phone.

Mimic is a different UI for the new League client that renders on your phone as a webpage instead of an application on your computer. It allows you to go through the game setup flow (from lobby until the end of champ select) from the safety of your toilet seat.

This repository contains the source code for Mimic. [Looking for the page with features and downloads instead?](https://mimic.lol)

## Autopick

In the lobby, **Setup Autopick** sets up champions per role: up to four to pick (a first choice and three backups, each with an optional skin, summoner spells and runes) and two to ban. The **Autopick** switch above it turns it on for your next game. It switches itself off when the game starts.

**All roles** is not a role: it fills in the picks or bans of any role that has none of its own, and is used in queues without roles. League Classic champions only show in the setup with the toggle above the champion list; in League Classic, autopick uses the Classic version of your champions by itself.

Autopick runs in Conduit on your PC, so it works while your phone is locked. It skips champions that are banned, taken, or wanted by a teammate, and leaves the pick or ban to you as soon as you choose something yourself. Runes go into the client's temporary rune page. If the client has no room for one, autopick uses a page named `FOR_MIMIC`. `MIMIC_AUTOPICK_LOCK_IN_DELAY` in `.env` makes it wait that many seconds into your turn before locking in.

## Developing Mimic

Mimic is composed of three different components: **web**, **conduit** and **rift**. Please read the appropriate READMEs in the subdirectories for information on how to develop for the platform.

- [**Web**](/web) is the user interface presented to users. It uses Vue with Typescript and handles the actual controlling of the client.

- [**Conduit**](/conduit) is the Windows application that redirects client traffic to the mobile website. It is written in C# and uses Websockets to connect to both the LCU and the mobile client.

- [**Rift**](/rift) is a Node/Express application that is responsible for tunneling a `Web <-> Conduit` connection through a central server. It also keeps track of the codes issued to clients (10 characters on this fork's Rift), doing so by signing JWT tokens. It does not get into contact with any raw data, since all traffic is end-to-end encrypted.

## License

Mimic and all of its components are released under the [MIT](https://github.com/molenzwiebel/Mimic/blob/master/LICENSE) license. Feel free to browse through the code as you like, and if you end up making any improvements or changes, please do not hesitate to make a pull request. :)
