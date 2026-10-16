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
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
