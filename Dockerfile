# One image: React build served from the API's wwwroot (same origin, no CORS).

FROM node:24-alpine AS web
WORKDIR /src
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/RekruTime.Api/RekruTime.Api.csproj RekruTime.Api/
RUN dotnet restore RekruTime.Api
COPY backend/RekruTime.Api/ RekruTime.Api/
RUN dotnet publish RekruTime.Api -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl for the healthcheck; /keys = Data Protection keys (mount a volume there so logins survive redeploys)
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*     && mkdir /keys && chown $APP_UID /keys
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/dist ./wwwroot
USER $APP_UID
# Always runs behind a reverse proxy (Caddy, nginx, PaaS): trust X-Forwarded-For/-Proto for client IP and https.
# ponytail: trusts any proxy – don't publish port 8080 to the internet directly.
ENV DataProtection__KeysPath=/keys     ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3     CMD curl -fsS http://localhost:8080/api/health || exit 1
ENTRYPOINT ["dotnet", "RekruTime.Api.dll"]
