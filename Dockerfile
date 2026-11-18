# syntax=docker/dockerfile:1
# The app's image: published with the .NET 10 SDK, run on the ASP.NET Core 10 runtime image, one process.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json surveyjs-aspnet-core.csproj ./
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore surveyjs-aspnet-core.csproj
COPY . .
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish surveyjs-aspnet-core.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Production settings as real environment variables, which win over appsettings.json. Listen on every interface, keep
# the data on the volume, and trust X-Forwarded-For and X-Forwarded-Proto from the proxy in front (README: Behind a proxy).
# The base image's ASPNETCORE_HTTP_PORTS=8080 is cleared: APP_HOST and APP_PORT decide where the app listens.
ENV ASPNETCORE_ENVIRONMENT=Production APP_HOST=0.0.0.0 APP_PORT=8000 DATA_DIR=/app/storage ASPNETCORE_FORWARDEDHEADERS_ENABLED=true     ASPNETCORE_HTTP_PORTS=

# The published app: the compiled migrations (the database is migrated and seeded on start, with no SDK and no
# dotnet ef), wwwroot with the SurveyJS packages' files, shared/, the manifest and the source files the Code tab shows
COPY --from=build /app/publish ./

# Not root: the image's app user, which only writes to /app/storage (database, uploads, sandboxes, relay snapshots)
RUN mkdir -p /app/storage && chown app:app /app/storage
USER app

EXPOSE 8000
# One process, PID 1 (exec form), so it gets SIGTERM and shuts down gracefully
ENTRYPOINT ["dotnet", "surveyjs-aspnet-core.dll"]
