FROM node:24-bookworm-slim AS build

WORKDIR /app

COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile

COPY tsconfig.json ./
COPY src ./src
RUN yarn bundle

FROM node:24-bookworm-slim

WORKDIR /app
ENV NODE_ENV=production

COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile --production && yarn cache clean
COPY --from=build /app/dist ./dist

# The database and generated JWT secret live in /data. Mount a volume there to keep them.
ENV RIFT_DATABASE=/data/database.db
RUN mkdir -p /data

EXPOSE 51001
CMD ["node", "dist/index.js"]
