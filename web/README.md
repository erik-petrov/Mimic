# :iphone: Mimic - Web

The web component for Mimic. Uses [Vue](https://vuejs.org), [TypeScript](https://www.typescriptlang.org) and [Stylus](http://stylus-lang.com). This is the actual component that powers the web application. It is served using a simple Nginx static host, with CloudFlare for caching.

## Development

You will need [Yarn](https://yarnpkg.com/lang/en/) for developing the web component.

After checking out the source, run `yarn install` to install all dependencies. You will only need to do this after pulling updates from Github.

During development, you can use `yarn serve` to start a webserver at [localhost:8080](http://localhost:8080). This webserver uses Hot Module Reloading to automatically refresh the UI whenever a file is edited.

Building a release bundle can be done using `yarn build`. This will generate a folder called `dist/` that contains all files needed to deploy.

Building is managed through vue-cli. It takes care of automatically optimizing, minifying, transpiling and everything else. On Node 17 and newer, set `NODE_OPTIONS=--openssl-legacy-provider` before building, since vue-cli 3 uses webpack 4.

## Hosting with Docker

`compose.yaml` in the repository root builds the app and serves it with nginx. From the repository root:

```
docker compose up -d --build
```

This serves the app on port 8080 (set `MIMIC_PORT` to change it). The app must be served over HTTPS, at the root of a (sub)domain: browsers only allow its encryption on secure pages, and the build loads its files from `/`. Point your reverse proxy at port 8080, or let the included Caddy handle HTTPS:

```
echo MIMIC_DOMAIN=mimic.example.com > .env
docker compose --profile caddy up -d --build
```

Caddy listens on ports 80 and 443 and gets a certificate for the domain automatically, so the domain must point at the server and those ports must be reachable. Then open `https://mimic.example.com/?code=123456` with the code from Conduit.

To update, pull the latest code and run the same `up` command again.

## License

-The web component of Mimic is released under the [MIT](https://github.com/molenzwiebel/Mimic/blob/master/LICENSE) license. See the index README for more info.