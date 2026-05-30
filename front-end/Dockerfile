# Stage 1: Build Angular
FROM node:18-alpine AS build-stage
WORKDIR /app

# Copiar dependencias primeiro para cache do npm install
COPY package.json package-lock.json ./
RUN npm ci --legacy-peer-deps

COPY . .

# Angular 12 + Node 18 requer flag de compatibilidade OpenSSL
ENV NODE_OPTIONS=--openssl-legacy-provider

RUN npx ng build --configuration production --output-hashing=all

# Stage 2: Servir com Nginx
FROM nginx:alpine AS runtime

RUN rm -rf /usr/share/nginx/html/*

COPY --from=build-stage /app/dist/ /usr/share/nginx/html/
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 80

ENTRYPOINT ["/docker-entrypoint.sh"]
