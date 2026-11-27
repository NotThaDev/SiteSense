# Build context: frontend/.
# Build the Vite bundle in a throwaway node stage.
FROM node:22.21.1-alpine AS build
WORKDIR /ui
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# Pinned nginx serves the bundle and proxies the API.
FROM nginx:1.29.3-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /ui/dist /usr/share/nginx/html
EXPOSE 3000
