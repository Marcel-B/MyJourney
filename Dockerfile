# Stufe 1: Frontend bauen (Vue + Vite)
FROM node:22-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# Stufe 2: Backend bauen und veröffentlichen
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY backend/MyJourney.Api/MyJourney.Api.csproj backend/MyJourney.Api/
RUN dotnet restore backend/MyJourney.Api/MyJourney.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/MyJourney.Api/MyJourney.Api.csproj -c Release -o /app/publish

# Stufe 3: Laufzeit-Image – das Backend liefert das Frontend aus wwwroot mit aus
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=backend-build /app/publish ./
COPY --from=frontend-build /src/frontend/dist ./wwwroot

ENV ASPNETCORE_URLS=http://+:8080
# SQLite-Datenbank auf einem Volume, damit sie Container-Neustarts überlebt
ENV ConnectionStrings__Journey="Data Source=/data/myjourney.db"
VOLUME /data
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyJourney.Api.dll"]
