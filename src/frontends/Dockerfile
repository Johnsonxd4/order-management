FROM node:22-bookworm-slim AS build
WORKDIR /app

COPY ["src/frontends/package.json", "src/frontends/package-lock.json*", "./"]
RUN npm install

COPY ["src/frontends/", "./"]

ARG PORTAL_KIND
ENV VITE_PORTAL_KIND=${PORTAL_KIND}

RUN npm run build

FROM nginx:1.27-alpine AS runtime
WORKDIR /usr/share/nginx/html

COPY ["deploy/nginx/default.conf", "/etc/nginx/conf.d/default.conf"]
COPY --from=build /app/dist .

EXPOSE 80
