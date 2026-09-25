FROM node:10-stretch

WORKDIR /app

COPY package.json yarn.lock ./
RUN yarn install --frozen-lockfile

COPY tsconfig.json ./
COPY src ./src
RUN yarn bundle

# The database and generated JWT secret live in /data. Mount a volume there to keep them.
ENV RIFT_DATABASE=/data/database.db
RUN mkdir -p /data

EXPOSE 51001
CMD ["node", "dist/index.js"]
