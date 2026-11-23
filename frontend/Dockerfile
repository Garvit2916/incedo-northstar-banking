# Build stage
FROM node:22-alpine AS build
WORKDIR /app

# Demo accounts are hidden in production builds unless explicitly enabled here.
# Never enable this for a build that will be exposed publicly.
ARG VITE_ENABLE_DEMO_ACCOUNTS=false
ENV VITE_ENABLE_DEMO_ACCOUNTS=$VITE_ENABLE_DEMO_ACCOUNTS

COPY frontend/package*.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

# Runtime stage
FROM nginx:1.27-alpine AS runtime

# The upstream is rendered at container start, so the same image works for local
# Compose (api:8080) and for the ECS sidecar pattern (127.0.0.1:8080).
ENV API_UPSTREAM=127.0.0.1:8080
# Restrict envsubst so nginx's own variables ($host, $remote_addr, ...) survive.
ENV NGINX_ENVSUBST_FILTER=API_UPSTREAM
COPY frontend/nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
