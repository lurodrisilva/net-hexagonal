# syntax=docker/dockerfile:1.9

# ---------------------------------------------------------------------------
# Build stage — runs natively on $BUILDPLATFORM, cross-compiles to $TARGETARCH.
# This keeps multi-arch builds (linux/amd64 + linux/arm64) fast because the
# .NET SDK itself never runs under QEMU emulation.
# ---------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# 1. Copy only files that affect `dotnet restore` so the restore layer caches
#    across source-only edits.
COPY global.json Directory.Build.props Directory.Packages.props Hex.Scaffold.slnx ./

COPY src/Hex.Scaffold.Domain/Hex.Scaffold.Domain.csproj                             src/Hex.Scaffold.Domain/
COPY src/Hex.Scaffold.Application/Hex.Scaffold.Application.csproj                   src/Hex.Scaffold.Application/
COPY src/Hex.Scaffold.Adapters.Inbound/Hex.Scaffold.Adapters.Inbound.csproj         src/Hex.Scaffold.Adapters.Inbound/
COPY src/Hex.Scaffold.Adapters.Outbound/Hex.Scaffold.Adapters.Outbound.csproj       src/Hex.Scaffold.Adapters.Outbound/
COPY src/Hex.Scaffold.Adapters.Persistence/Hex.Scaffold.Adapters.Persistence.csproj src/Hex.Scaffold.Adapters.Persistence/
COPY src/Hex.Scaffold.Api/Hex.Scaffold.Api.csproj                                   src/Hex.Scaffold.Api/

# 2. Restore into a BuildKit cache mount — NuGet packages survive across builds
#    and never land in a layer. Cache id is arch-scoped so the amd64 and arm64
#    build stages don't race on writes to the same .nuget/packages tree.
#    NOTE: `dotnet restore` intentionally does NOT pass `-a $TARGETARCH`. With
#    that flag, some .NET SDK versions skip architecture-neutral analyzer
#    packages (e.g. Mediator.SourceGenerator), which then break the publish
#    step below with NETSDK1064 "Package … was not found". A full restore
#    fetches everything; publish picks the RID-specific runtime slice.
RUN --mount=type=cache,id=nuget-${TARGETARCH},target=/root/.nuget/packages \
    dotnet restore src/Hex.Scaffold.Api/Hex.Scaffold.Api.csproj

# 3. Copy the rest of the source and publish straight to /out.
#    `dotnet publish` will build as needed; a separate `build` stage is redundant.
COPY src/ src/

#    NOTE: `--no-restore` is deliberately omitted. The arch-agnostic restore
#    above produces a portable project.assets.json without a
#    `net10.0/linux-$RID` target; publish's own RID-aware restore fills that in
#    (cheap — packages are already in the cache mount). Adding --no-restore
#    here fails with NETSDK1047 "doesn't have a target for net10.0/linux-<rid>".
RUN --mount=type=cache,id=nuget-${TARGETARCH},target=/root/.nuget/packages \
    dotnet publish src/Hex.Scaffold.Api/Hex.Scaffold.Api.csproj \
        -c Release \
        -a $TARGETARCH \
        --no-self-contained \
        -o /out

# 4. Produce a self-contained EF Core migration bundle. The runtime image is
#    chiseled (no shell, no SDK), so `dotnet ef database update` cannot run in
#    the pod. `dotnet ef migrations bundle --self-contained` emits a single
#    executable that embeds the migrations + a minimal .NET runtime and reads
#    its connection string from ConnectionStrings__PostgreSql via the startup
#    project's IConfiguration. Shipped as /app/efbundle in the final image.
RUN --mount=type=cache,id=nuget-${TARGETARCH},target=/root/.nuget/packages \
    case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet tool install --tool-path /dotnet-tools dotnet-ef --version 10.0.0 && \
    /dotnet-tools/dotnet-ef migrations bundle \
        --project src/Hex.Scaffold.Adapters.Persistence/Hex.Scaffold.Adapters.Persistence.csproj \
        --self-contained \
        --target-runtime $RID \
        --output /efbundle/efbundle \
        --configuration Release

# ---------------------------------------------------------------------------
# Runtime stage — Microsoft "chiseled" image (distroless-equivalent).
# No shell, no package manager, pre-set non-root user $APP_UID=1654.
# The `-extra` variant ships ICU + tzdata for apps that need globalization.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

COPY --from=build /out .
# Ship the self-contained EF migration bundle alongside the app. Invoked by
# the Helm pre-install/pre-upgrade Job (see deploy/helm/.../migration-job.yaml).
COPY --from=build /efbundle/efbundle /app/efbundle

USER $APP_UID

# Chiseled images have no shell / curl, so HEALTHCHECK is delegated to the
# orchestrator (Kubernetes livenessProbe / readinessProbe hitting /health).
# If you need a baked-in HEALTHCHECK, add a self-check endpoint and ship a
# small statically-linked probe binary in this stage.

ENTRYPOINT ["dotnet", "Hex.Scaffold.Api.dll"]
