# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a project file changes.
COPY Directory.Build.props ./
COPY src/LifeOS.Domain/LifeOS.Domain.csproj src/LifeOS.Domain/
COPY src/LifeOS.Application/LifeOS.Application.csproj src/LifeOS.Application/
COPY src/LifeOS.Infrastructure/LifeOS.Infrastructure.csproj src/LifeOS.Infrastructure/
COPY src/LifeOS.Web/LifeOS.Web.csproj src/LifeOS.Web/
RUN dotnet restore src/LifeOS.Web/LifeOS.Web.csproj

COPY src/ src/
RUN dotnet publish src/LifeOS.Web/LifeOS.Web.csproj -c Release -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# /data holds the SQLite database, Data Protection keys and backups. Mount a volume here.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    LifeOS__DataPath=/data \
    DOTNET_gcServer=0

RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

USER $APP_UID
VOLUME ["/data"]
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "LifeOS.Web.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "LifeOS.Web.dll"]
