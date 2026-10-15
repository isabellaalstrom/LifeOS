# Home Assistant app image. The Supervisor builds this with the app folder as build context, so
# ha/build-local-app.ps1 stages this file together with src/ and Directory.Build.props.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/LifeOS.Domain/LifeOS.Domain.csproj src/LifeOS.Domain/
COPY src/LifeOS.Application/LifeOS.Application.csproj src/LifeOS.Application/
COPY src/LifeOS.Infrastructure/LifeOS.Infrastructure.csproj src/LifeOS.Infrastructure/
COPY src/LifeOS.Web/LifeOS.Web.csproj src/LifeOS.Web/
RUN dotnet restore src/LifeOS.Web/LifeOS.Web.csproj
COPY src/ src/
RUN dotnet publish src/LifeOS.Web/LifeOS.Web.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# HA mounts /data root-owned, so this image runs as root inside the add-on sandbox (standard for HA apps).
# Ingress gateway (172.30.32.2) is the only peer trusted to supply X-Ingress-Path / X-Forwarded-*.
ENV ASPNETCORE_URLS=http://+:8099 \
    ASPNETCORE_ENVIRONMENT=Production \
    LifeOS__DataPath=/data \
    LifeOS__Ingress__Enabled=true

EXPOSE 8099
ENTRYPOINT ["dotnet", "LifeOS.Web.dll"]
