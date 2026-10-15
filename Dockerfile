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
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/dist ./wwwroot
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "RekruTime.Api.dll"]
