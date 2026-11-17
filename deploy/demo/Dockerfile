# The public demo in one container: the API serves the built web app from wwwroot, and a Redis inside the container
# holds presence and the backplane, so a single free web service (Render) runs the whole thing next to a hosted
# PostgreSQL (Neon). Compose keeps nginx, two API instances and its own Redis instead.
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props RealtimeChat.slnx ./
COPY src/ src/
RUN dotnet publish src/RealtimeChat.Api/RealtimeChat.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends redis-server && rm -rf /var/lib/apt/lists/*
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Redis=127.0.0.1:6379
EXPOSE 8080
COPY --from=build /app/publish .
COPY --from=web /web/dist ./wwwroot
COPY deploy/demo/start.sh /start.sh
USER $APP_UID
ENTRYPOINT ["/start.sh"]
