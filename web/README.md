# :iphone: Mimic - Web

The web component for Mimic. Uses [Vue](https://vuejs.org), [TypeScript](https://www.typescriptlang.org) and [Stylus](http://stylus-lang.com). This is the actual component that powers the web application. It is served using a simple Nginx static host, with CloudFlare for caching.

## Development

You will need [Yarn](https://yarnpkg.com/lang/en/) for developing the web component.

After checking out the source, run `yarn install` to install all dependencies. You will only need to do this after pulling updates from Github.

During development, you can use `yarn serve` to start a webserver at [localhost:8080](http://localhost:8080). This webserver uses Hot Module Reloading to automatically refresh the UI whenever a file is edited.

Building a release bundle can be done using `yarn build`. This will generate a folder called `dist/` that contains all files needed to deploy.

Building is managed through vue-cli. It takes care of automatically optimizing, minifying, transpiling and everything else. On Node 17 and newer, set `NODE_OPTIONS=--openssl-legacy-provider` before building, since vue-cli 3 uses webpack 4.

## Hosting with Docker

`compose.yaml` in the repository root runs the app together with [Rift](/rift) on one address: the app at `/`, Rift at `/mobile`, `/conduit`, `/register` and `/check`. From the repository root:

```
docker compose up -d --build
```

This serves both on port 8080 (set `MIMIC_PORT` in a `.env` file to change it). Serve it over HTTPS at the root of a (sub)domain, with websockets allowed: browsers only allow the app's encryption on secure pages, and the build loads its files from `/`. Point your reverse proxy at the port, or let the included Caddy handle HTTPS:

```
echo MIMIC_DOMAIN=mimic.example.com > .env
docker compose --profile caddy up -d --build
```

Caddy listens on ports 80 and 443 and gets a certificate for the domain automatically, so the domain must point at the server and those ports must be reachable.

Then point Conduit at your server: open Conduit's Settings from its tray icon, enter your address (like `https://mimic.example.com`) under Server and press Use. Conduit checks that a Mimic server answers there, then gets a new code from it once League is running; its QR code opens your address. Shared goes back to the shared server. (The address is kept in `%APPDATA%\Mimic\server`.) To keep using the shared Rift instead, set `MIMIC_RIFT_URL=wss://rift.mimic.lol` in `.env`.

To update, pull the latest code and run the same `up` command again. Codes survive updates in the `rift_data` volume.

## License

-The web component of Mimic is released under the [MIT](https://github.com/molenzwiebel/Mimic/blob/master/LICENSE) license. See the index README for more info.