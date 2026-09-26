# :sparkles: Mimic - Rift

The server-side tunneling components for Mimic that faciliates communication between the mobile clients and Conduit instances without those Conduit instances needing to be exposed to the internet. This is also the component responsible for assigning unique identifiers to Conduit instances for them to identify themselves. All traffic going through Rift is encrypted; Rift (and thus whoever is hosting Rift) cannot read your messages or other sensitive traffic.

Rift is built using [Node.js](https://nodejs.org) and [TypeScript](https://www.typescriptlang.org). The database is SQLite, through the `node:sqlite` module built into Node.

## Development

You will need [Yarn](https://yarnpkg.com/lang/en/) 1 for developing the Rift component, and Node 22.13 or newer for the built-in SQLite module. The Docker image uses Node 24.

After checking out the source, run `yarn install` to install all dependencies. You will only need to do this after pulling updates from Github.

During development, you can use `yarn watch` to automatically compile TypeScript files once they are edited. However, it is recommended to simply use `yarn start` to start the application, since this will also compile all TypeScript files into Javascript files.

`yarn bundle` acts the same as `yarn watch`, except it will only compile the files once and not listen for edits.

## Codes

Rift gives each Conduit a code of 10 characters: digits and capital letters, without 0, O, 1, I and L. Phones can type it in any case, with spaces or dashes. Rift removes the 6 digit codes of older versions when it starts; their Conduits register again and get a new code the next time they connect.

## Configuration

- `PORT`: port to listen on, 51001 by default.
- `RIFT_DATABASE`: path of the SQLite database, `database.db` by default. The Docker image uses `/data/database.db`.
- `RIFT_JWT_SECRET`: secret that signs the tokens given to Conduit. If unset, Rift creates one next to the database and reuses it, so codes stay valid across restarts.
- `AUTOPICK_LOCK_IN_DELAY`: seconds autopick waits into your turn before it locks in or bans, `0` by default. Conduit reads it from `GET /settings`. It always locks in a few seconds before the turn ends.

See `compose.yaml` in the repository root to run Rift together with the web app.

## License

The Rift component of Mimic is released under the [MIT](https://github.com/molenzwiebel/Mimic/blob/master/LICENSE) license. See the index README for more info.