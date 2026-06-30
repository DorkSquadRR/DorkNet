# DorkNet.Server container image — Coolify deploy target.
# ────────────────────────────────────────────────────────────────────
# Multi-stage: SDK image builds + publishes, runtime image is the
# slimmer aspnet:10.0 with only the published output.
#
# Build context: repo root (so Tools/ and DorkNet.Models/ are visible
# to ProjectReference + dotnet restore). The .dockerignore at the
# repo root keeps the bin/, obj/, and other dev junk out of the
# context.

ARG DOTNET_VERSION=10.0
ARG NODE_VERSION=20

# ── admin SPA build stage ──────────────────────────────────────────
# The Vite + React admin UI under DorkNet.Server/admin-ui builds in a
# dedicated node image because the .NET SDK image doesn't ship npm.
# Output is dropped into /wwwroot/admin and copied into the .NET build
# stage just before `dotnet publish`, which is configured to skip its
# own inline npm step via -p:SkipAdminUIBuild=true.
FROM node:${NODE_VERSION}-alpine AS admin-ui
WORKDIR /ui
# Lockfile first for layer caching: when only application source
# changes, the npm install step stays cached.
COPY DorkNet.Server/admin-ui/package.json DorkNet.Server/admin-ui/package-lock.json* ./
RUN npm install --no-fund --no-audit --loglevel=error
# Now the source. vite.config.ts writes to ../wwwroot/admin/, which
# from /ui is /wwwroot/admin/ — pre-create the parent so Vite's
# emptyOutDir won't refuse to create above its cwd.
COPY DorkNet.Server/admin-ui/ ./
RUN mkdir -p /wwwroot/admin \
 && npm run build \
 && ls -la /wwwroot/admin \
 && ls -la /wwwroot/admin/assets

# ── public-facing site build stage ─────────────────────────────────
# Mirrors the admin-ui stage. Source under DorkNet.Server/site,
# output emitted to /wwwroot/site for the .NET stage to copy.
FROM node:${NODE_VERSION}-alpine AS site
WORKDIR /ui
COPY DorkNet.Server/site/package.json DorkNet.Server/site/package-lock.json* ./
RUN npm install --no-fund --no-audit --loglevel=error
COPY DorkNet.Server/site/ ./
RUN mkdir -p /wwwroot/site \
 && npm run build \
 && ls -la /wwwroot/site

# ── build stage ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Copy csproj files first so `dotnet restore` is cached when only
# source files change. Web SDK pulls in transitive packages from the
# DorkNet.Models project ref, so its csproj needs to be present too.
COPY DorkNet.Server/DorkNet.Server.csproj DorkNet.Server/
COPY DorkNet.Models/DorkNet.Models.csproj DorkNet.Models/
RUN dotnet restore DorkNet.Server/DorkNet.Server.csproj

# Now copy the rest of the .NET source, then drop the prebuilt admin
# SPA into wwwroot/admin so the publish step ships it as static content.
COPY DorkNet.Server/ DorkNet.Server/
COPY DorkNet.Models/ DorkNet.Models/
COPY --from=admin-ui /wwwroot/admin/ DorkNet.Server/wwwroot/admin/
COPY --from=site      /wwwroot/site/  DorkNet.Server/wwwroot/site/

# Publish. SkipAdminUIBuild=true bypasses the csproj target that would
# otherwise try to invoke npm (which this image doesn't have).
RUN dotnet publish DorkNet.Server/DorkNet.Server.csproj \
    -c Release \
    --no-restore \
    -p:SkipAdminUIBuild=true \
    -o /app/publish

# ── runtime stage ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# curl + wget for healthcheck probes — Coolify v4 defaults to wget for
# its own probe regardless of what the image's HEALTHCHECK uses, so
# both have to be present. The aspnet:10.0 base image strips both by
# default to keep the surface area small.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl wget \
    && rm -rf /var/lib/apt/lists/*

# Run as a non-root user — aspnet:10.0 ships with the `app` UID 1654.
# Coolify mounts persistent volumes with write permission for this UID.
USER app

COPY --from=build --chown=app:app /app/publish .

# Postgres provider is selected by appsettings.Production.json.
# ASPNETCORE_ENVIRONMENT defaults to Production in the aspnet image
# but we set it explicitly for clarity.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_PRINT_TELEMETRY_MESSAGE=false

EXPOSE 8080

# /healthz returns 503 until the migration + seed pass complete on
# the singleton holding the pg_advisory_xact_lock; other replicas
# block on the lock during the same window. Coolify's default 30s
# probe interval is fine.
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "DorkNet.Server.dll"]
