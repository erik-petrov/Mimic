# Builds the Mimic web app and serves it with nginx on port 80.
# Put it behind a reverse proxy that provides HTTPS: the app needs a secure page for its encryption.
FROM node:22-alpine AS build

WORKDIR /app
COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile

COPY . .
# vue-cli 3 uses webpack 4, which needs the legacy OpenSSL provider on Node 17 and newer.
RUN NODE_OPTIONS=--openssl-legacy-provider yarn build

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
