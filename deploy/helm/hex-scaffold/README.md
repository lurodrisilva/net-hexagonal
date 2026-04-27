# hex-scaffold Helm chart

A production-shaped chart for the `hex-scaffold` .NET 10 microservice. A single chart instance deploys **one** inbound adapter, **one** outbound adapter, and **one** primary persistence store (optionally + Redis), with the selection driven entirely by the chart's ConfigMap.

- [Chart source](./)
- [TL;DR](#tldr)
- [Prerequisites](#prerequisites)
- [Installing the chart](#installing-the-chart)
- [Uninstalling the chart](#uninstalling-the-chart)
- [The ConfigMap-driven selector contract](#the-configmap-driven-selector-contract)
- [What the chart renders per feature combination](#what-the-chart-renders-per-feature-combination)
- [Parameter reference](#parameter-reference)
  - [Image parameters](#image-parameters)
  - [Common parameters](#common-parameters)
  - [Feature selector (ConfigMap)](#feature-selector-configmap)
  - [Environment variable overrides](#environment-variable-overrides)
  - [Secrets parameters](#secrets-parameters)
  - [Application parameters](#application-parameters)
  - [WireMock parameters](#wiremock-parameters)
  - [Database migration parameters](#database-migration-parameters)
  - [Service parameters](#service-parameters)
  - [Ingress parameters](#ingress-parameters)
  - [Resources and probes](#resources-and-probes)
  - [Autoscaling parameters](#autoscaling-parameters)
  - [Pod metadata and security parameters](#pod-metadata-and-security-parameters)
  - [Observability parameters](#observability-parameters)
- [Secrets management in production](#secrets-management-in-production)
- [Upgrading the chart](#upgrading-the-chart)
- [Developer reference: how the selector is wired into the Deployment](#developer-reference-how-the-selector-is-wired-into-the-deployment)

---

## TL;DR

```bash
helm upgrade --install hex-scaffold ./deploy/helm/hex-scaffold \
  --namespace default --create-namespace \
  --set features.inbound=rest \
  --set features.outbound=kafka \
  --set features.persistence=postgres \
  --set features.redis=true \
  --set secrets.appInsightsConnectionString="$APP_INSIGHTS_CS"
```

## Prerequisites

- Kubernetes ≥ **1.27** (the chart declares `kubeVersion: ">=1.27.0"` in `Chart.yaml`).
- Helm ≥ **3.12**.
- When `features.persistence=postgres`: a reachable PostgreSQL 14+ instance and a user with permission to create/alter tables (for the migration Job).
- When `features.persistence=mongo`: a reachable MongoDB 6+ instance.
- When `features.redis=true`: a reachable Redis 7+ instance.
- When any `features.*=kafka`: a reachable Kafka cluster (Strimzi recommended). The topic `v2.core.accounts` (the `AccountEventPublishHandler` / `AccountEventConsumer` contract) must exist; the chart does **not** provision it.
- For Application Insights: an existing workspace-based Application Insights resource and its connection string.

## Installing the chart

```bash
helm upgrade --install hex-scaffold ./deploy/helm/hex-scaffold \
  --namespace default --create-namespace \
  -f my-values.yaml
```

A typical `my-values.yaml` only needs to override the feature selector and secrets:

```yaml
features:
  inbound: rest
  outbound: kafka
  persistence: postgres
  redis: true

secrets:
  postgresConnectionString: "Host=pg;Database=hex;Username=app;Password=...;Port=5432"
  kafkaBootstrapServers:    "my-cluster-kafka-bootstrap.kafka.svc:9092"
  appInsightsConnectionString: "InstrumentationKey=...;IngestionEndpoint=..."
```

## Uninstalling the chart

```bash
helm uninstall hex-scaffold
```

The migration Job is garbage-collected via the `helm.sh/hook-delete-policy: before-hook-creation,hook-succeeded` annotation. Secrets created by the chart are deleted; secrets referenced from external systems (CSI / ExternalSecrets) are not touched.

---

## The ConfigMap-driven selector contract

The ConfigMap rendered by [`templates/configmap.yaml`](./templates/configmap.yaml) is **the** knob for adapter selection. Its `data:` keys map to .NET configuration keys using the double-underscore delimiter:

| ConfigMap key | Maps to | Allowed values |
|---------------|---------|----------------|
| `Features__InboundAdapter` | `Features:InboundAdapter` | `rest` · `kafka` |
| `Features__OutboundAdapter` | `Features:OutboundAdapter` | `rest` · `kafka` |
| `Features__Persistence` | `Features:Persistence` | `postgres` · `mongo` |
| `Features__UseRedis` | `Features:UseRedis` | `true` · `false` (only `true` when `Persistence=postgres`) |

The Deployment mounts this ConfigMap via `envFrom`. At startup, `FeaturesOptions.Validate()` in [`src/Hex.Scaffold.Api/Options/FeaturesOptions.cs`](../../../src/Hex.Scaffold.Api/Options/FeaturesOptions.cs) re-enforces the invariants, so any bad env-var override also fails fast.

### Validation layers

1. **Helm render time** — `templates/_helpers.tpl::hex-scaffold.validateFeatures` aborts the template with a readable error before anything is sent to the API server.
2. **Pod startup** — `FeaturesOptions.Validate()` throws `InvalidOperationException` if the env vars disagree with the schema (e.g. a misconfigured SecretProviderClass mutates them).

### Rules

| # | Rule |
|---|------|
| 1 | Exactly one inbound adapter (`rest` **or** `kafka`). |
| 2 | Exactly one outbound adapter (`rest` **or** `kafka`). |
| 3 | Exactly one primary store (`postgres` **or** `mongo`). |
| 4 | `redis=true` is only legal when `persistence=postgres`. MongoDB + Redis is explicitly rejected. |

## What the chart renders per feature combination

| Resource | Condition |
|----------|-----------|
| `ConfigMap` | **always** |
| `Secret` | **always** — keys inside the secret are conditional on features |
| `Deployment` | **always** |
| `Service` | `features.inbound = rest` |
| `Ingress` | `features.inbound = rest` AND `ingress.enabled = true` |
| `HorizontalPodAutoscaler` | `autoscaling.enabled = true` |
| `Job` (migration) | `features.persistence = postgres` AND `migrations.enabled = true` (Helm `pre-install` / `pre-upgrade` hook, weight `-5`) |
| `Deployment` (wiremock) | `wiremock.enabled = true` |
| `Service` (wiremock) | `wiremock.enabled = true` |
| `ConfigMap` (wiremock mappings) | `wiremock.enabled = true` |

The runtime `ConfigMap` and `Secret` carry hook annotations (`pre-install,pre-upgrade`, weight `-10`) so they are applied **before** the migration Job (weight `-5`), which `envFrom`s them. `before-hook-creation` keeps them in the cluster after the hook runs so the regular Deployment can also mount them; `helm uninstall` still removes them (no `resource-policy=keep`).

---

## Parameter reference

All defaults below come from [`values.yaml`](./values.yaml). Types follow the Helm convention (`string`, `int`, `bool`, `list`, `map`). Parameters marked **—required**— must be overridden in production; the shipped defaults are suitable for demo/dev only.

### Image parameters

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `image.repository` | string | `ghcr.io/lurodrisilva/net-hexagonal` | Container image repository. |
| `image.tag` | string | `""` | Image tag; falls back to `.Chart.AppVersion` when empty. Override for pinned releases. |
| `image.pullPolicy` | string | `Always` | Kubernetes image pull policy. Defaults to `Always` because the shipped `image.tag` is `latest` (mutable) — the chart would otherwise serve stale cached digests on AKS nodes after a CI push. Override to `IfNotPresent` when pinning to an immutable semver or sha tag. |
| `imagePullSecrets` | list(map) | `[]` | List of secret names granting pull access to private registries. |

### Common parameters

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `replicaCount` | int | `2` | Number of pod replicas (ignored when `autoscaling.enabled=true`). |
| `nameOverride` | string | `""` | Overrides the chart's template name (rarely needed). |
| `fullnameOverride` | string | `""` | Overrides the full release name used for every resource. |

### Feature selector (ConfigMap)

| Key | Type | Default | Allowed | Description |
|-----|------|---------|---------|-------------|
| `features.inbound` | string | `rest` | `rest` · `kafka` | Which inbound adapter receives work. REST exposes the `/v2/core/accounts` Stripe v2 Accounts API; Kafka runs the `AccountEventConsumer` BackgroundService. |
| `features.outbound` | string | `kafka` | `rest` · `kafka` | Which outbound adapter publishes domain events. REST falls back to `NoOpEventPublisher` (events are dropped); Kafka uses `KafkaEventPublisher` (topic `v2.core.accounts`). |
| `features.persistence` | string | `postgres` | `postgres` · `mongo` | Primary store. Postgres uses EF Core (with `NpgsqlDataSourceBuilder.EnableDynamicJson()` for the Account aggregate's jsonb columns). Mongo currently registers `IMongoClient` only — no read-model is wired in this scaffold revision. |
| `features.redis` | bool | `true` | `true` · `false` | Registers `RedisCacheService` (or `NullCacheService` when `false`). Must be `false` when `persistence=mongo` (chart fails fast). |

### Environment variable overrides

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `env` | list(map) | `[]` | Extra container env vars, merged after `envFrom` ConfigMap/Secret (so they override). Use sparingly — prefer the typed knobs above. Each entry: `{ name, value }` or `{ name, valueFrom }`. |

### Secrets parameters

All rendered into a single `Secret` (stringData). Conditional keys: a key is only emitted when the feature that needs it is active.

| Key | Type | Default | Required when | Description |
|-----|------|---------|---------------|-------------|
| `secrets.postgresConnectionString` | string | demo value | `features.persistence=postgres` | Standard ADO-style connection string (`Host=…;Database=…;Username=…;Password=…;Port=5432`). |
| `secrets.mongoConnectionString` | string | `mongodb://mongo:27017` | `features.persistence=mongo` | Mongo URI. The app appends/expects `MongoDB__DatabaseName` separately (see `application.mongoDatabaseName`). |
| `secrets.redisConnectionString` | string | `redis:6379` | `features.redis=true` | StackExchange.Redis connection string. |
| `secrets.kafkaBootstrapServers` | string | `kafka-bootstrap.kafka.svc:9092` | any `features.*=kafka` | Comma-separated `host:port` list. |
| `secrets.kafkaConsumerGroupId` | string | `hex-scaffold-group` | `features.inbound=kafka` | Consumer group id. Usually per-env, never per-replica. Rendered into the ConfigMap, not the Secret. |
| `secrets.externalApiBaseUrl` | string | `https://httpbin.org` | only when `wiremock.enabled=false` | Base URL for the resilient outbound HTTP client. **Ignored** when `wiremock.enabled=true` — the chart helper `hex-scaffold.externalApiBaseUrl` then rewrites `ExternalApi__BaseUrl` to point at the in-cluster WireMock service so the app hits a deterministic mock with a baked-in delay. See [WireMock parameters](#wiremock-parameters). |
| `secrets.appInsightsConnectionString` | string | `""` | to activate Azure Monitor | Non-empty value activates the Azure Monitor OTel exporter in code. Empty = OTLP-only. **Prod: populate via CSI / ExternalSecrets.** |

### Application parameters

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `application.port` | int | `8080` | Container listen port. Matches the Dockerfile `EXPOSE 8080`. |
| `application.mongoDatabaseName` | string | `hex-scaffold` | `MongoDB__DatabaseName`. Ignored when `features.persistence != mongo`. |
| `application.environment` | string | `Production` | `ASPNETCORE_ENVIRONMENT`. Values: `Development`, `Staging`, `Production`. |

### WireMock parameters

When `wiremock.enabled=true` (the default), the chart renders an in-cluster WireMock Deployment + Service + ConfigMap and rewrites the application's `ExternalApi__BaseUrl` to target it. The outbound REST adapter (`IExternalApiClient`) then hits a deterministic mock with a baked-in latency instead of an external host. Toggle off when you point at a real upstream (`secrets.externalApiBaseUrl`).

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `wiremock.enabled` | bool | `true` | Render WireMock resources and rewrite `ExternalApi__BaseUrl` to the WireMock service. |
| `wiremock.replicaCount` | int | `1` | Replica count. WireMock is stateless; horizontal scaling is rarely needed for mock workloads. |
| `wiremock.image.repository` | string | `wiremock/wiremock` | Container image. |
| `wiremock.image.tag` | string | `3.13.2-3-alpine` | WireMock 3.x on Alpine — slim and arch-portable. Pinned to an immutable patched tag because the floating `3-alpine` alias was returning 404 from Docker Hub in some pull contexts. |
| `wiremock.image.pullPolicy` | string | `IfNotPresent` | Image pull policy (immutable tag). |
| `wiremock.service.port` | int | `8080` | Service port. The chart helper `hex-scaffold.externalApiBaseUrl` builds the URL `http://<release>-wiremock:<port>` from this. |
| `wiremock.resources` | map | `50m/128Mi` requests, `500m/256Mi` limits | Standard `resources` block. |
| `wiremock.fixedDelayMs` | int | `300` | Global response delay (ms) injected into every stub's `response` at render time so the application's HttpClient resilience pipeline observes a realistic latency. Set in one place — do **not** add `fixedDelayMilliseconds` per stub. |
| `wiremock.mappings` | map of stub objects | catchall `ANY .*` → `200` JSON | Stub mappings rendered as individual JSON files (`/home/wiremock/mappings/<key>.json`) and loaded by WireMock at startup. Each entry follows the standard WireMock JSON shape (`request`, `response`). The chart adds `fixedDelayMilliseconds: <fixedDelayMs>` into every `response` automatically. |

The deployment runs WireMock with `--global-response-templating`, so stub bodies can use Handlebars helpers (`{{request.path}}`, `{{request.headers.X-...}}`, etc.). The pod template carries `checksum/mappings` so any change to `wiremock.mappings` rolls the pod on `helm upgrade`.

### Database migration parameters

The runtime image ships a self-contained EF Core migration bundle at `/app/efbundle` (built in the Dockerfile via `dotnet ef migrations bundle --self-contained --target-runtime $RID`). The migration Job invokes that binary directly — no shell, no SDK in the runtime image. The bundle reads `ConnectionStrings__PostgreSql` from the Job's `envFrom` Secret via the same `IDesignTimeDbContextFactory<AppDbContext>` the app uses at design time, so the connection string never appears on the command line.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `migrations.enabled` | bool | `true` | Enables the EF Core migration Job (Helm `pre-install`/`pre-upgrade` hook, weight `-5`). Ignored when `features.persistence != postgres`. |
| `migrations.image.repository` | string | `ghcr.io/lurodrisilva/net-hexagonal` | Migration-runner image. Defaults to the app image because `/app/efbundle` is shipped inside it; override only if you publish a dedicated migration image variant. |
| `migrations.image.tag` | string | `""` | Falls back to `.Chart.AppVersion`. Keep this in lockstep with `image.tag` so the bundle's embedded migrations match the app's expected schema. |
| `migrations.image.pullPolicy` | string | `Always` | Same rationale as `image.pullPolicy` — defaults to `Always` because `:latest` is a mutable tag. |
| `migrations.backoffLimit` | int | `3` | Job `backoffLimit`. |
| `migrations.activeDeadlineSeconds` | int | `600` | Job `activeDeadlineSeconds` (hard upper bound on migration duration). |

### Service parameters

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `service.type` | string | `ClusterIP` | Only `ClusterIP`, `NodePort`, `LoadBalancer` are meaningful for this app. |
| `service.port` | int | `80` | Service-side port. |
| `service.targetPort` | int | `8080` | Pod-side port (match `application.port`). |
| `service.annotations` | map | `{}` | Service annotations (e.g. AWS LB, Azure internal LB). |

### Ingress parameters

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `ingress.enabled` | bool | `false` | Renders the Ingress resource. Requires `features.inbound=rest`. |
| `ingress.className` | string | `""` | `ingressClassName` (e.g. `nginx`, `traefik`, `application-gateway`). |
| `ingress.annotations` | map | `{}` | Controller-specific annotations (cert-manager, TLS redirect, WAF, etc.). |
| `ingress.hosts[].host` | string | `hex-scaffold.local` | Hostname to serve. |
| `ingress.hosts[].paths[].path` | string | `/` | Path. |
| `ingress.hosts[].paths[].pathType` | string | `Prefix` | `Prefix` \| `Exact` \| `ImplementationSpecific`. |
| `ingress.tls` | list(map) | `[]` | Standard Ingress TLS list; each entry `{ secretName, hosts }`. |

### Resources and probes

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `resources.requests.cpu` | string | `100m` | Baseline CPU. |
| `resources.requests.memory` | string | `256Mi` | Baseline memory. |
| `resources.limits.cpu` | string | `"1"` | CPU ceiling; tune per profile. |
| `resources.limits.memory` | string | `512Mi` | Memory ceiling; the `.NET` runtime honors cgroup limits for heap sizing. |
| `livenessProbe` | map | HTTP `/healthz`, 10s delay, 15s period, 3 failures | Full Kubernetes probe object — override whole object to change scheme/command. |
| `readinessProbe` | map | HTTP `/ready`, 5s delay, 10s period, 3 failures | Readiness probe includes Postgres / Mongo / Redis / Kafka (soft) checks. |

### Autoscaling parameters

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `autoscaling.enabled` | bool | `false` | Renders an HPA. When true, `replicaCount` is ignored. |
| `autoscaling.minReplicas` | int | `2` | HPA floor. |
| `autoscaling.maxReplicas` | int | `10` | HPA ceiling. |
| `autoscaling.targetCPUUtilizationPercentage` | int | `70` | CPU target. Set to `null` to disable the CPU metric. |
| `autoscaling.targetMemoryUtilizationPercentage` | int | `80` | Memory target. Set to `null` to disable. |

### Pod metadata and security parameters

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `podAnnotations` | map | `{}` | Extra pod annotations. Checksums for ConfigMap + Secret are always added automatically so the Deployment rolls on config changes. |
| `podLabels` | map | `{}` | Extra pod labels. |
| `podSecurityContext.runAsNonRoot` | bool | `true` | Matches the chiseled .NET image's `$APP_UID` = 1654. |
| `podSecurityContext.fsGroup` | int | `1654` | Volume-group used for the `/tmp` emptyDir. |
| `securityContext.allowPrivilegeEscalation` | bool | `false` | — |
| `securityContext.readOnlyRootFilesystem` | bool | `true` | `/tmp` is writable via an emptyDir volume mount. |
| `securityContext.runAsUser` | int | `1654` | — |
| `securityContext.capabilities.drop` | list | `[ALL]` | — |
| `nodeSelector` | map | `{}` | Standard pod `nodeSelector`. |
| `tolerations` | list | `[]` | Standard pod `tolerations`. |
| `affinity` | map | `{}` | Standard pod `affinity`. |

### Observability parameters

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `observability.otlp.endpoint` | string | `http://otel-collector.observability.svc:4318` | OTLP HTTP receiver URL. Rendered into `OpenTelemetry__OtlpEndpoint`. Leave as default when running an OTel Collector sidecar/DaemonSet; point elsewhere for managed backends. |

> Azure Monitor ingest is activated purely by setting `secrets.appInsightsConnectionString`; there are no further observability knobs. The four golden signals (latency / traffic / errors / saturation) are emitted automatically by the wired OpenTelemetry instrumentations. See [`docs/observability.md`](../../../docs/observability.md) and [`docs/loadtest.md`](../../../docs/loadtest.md#4-validating-results-in-application-insights) for the KQL recipes.

---

## Secrets management in production

The `secret.yaml` template ships a `Secret` populated from `values.yaml`, which is **demo-grade only**. For production, replace it with one of:

| Approach | What you do |
|----------|-------------|
| **Azure Key Vault + CSI driver** | Create a `SecretProviderClass` projecting Key Vault secrets as `Secret` entries; reference it via `envFrom` or mounted files. Remove/disable `templates/secret.yaml`. |
| **External Secrets Operator** | Author an `ExternalSecret` + `ClusterSecretStore` for Key Vault / Vault / AWS / GCP; it generates the `Secret` the Deployment's `envFrom` already points at. |
| **Sealed Secrets** | `kubeseal` the values and commit the `SealedSecret` alongside the release. The controller renders a `Secret` with the name the chart expects. |

Regardless of the backend, the app needs these keys present as env vars:

| Env var | Populated when |
|---------|----------------|
| `ConnectionStrings__PostgreSql` | `features.persistence=postgres` |
| `MongoDB__ConnectionString` | `features.persistence=mongo` |
| `Redis__ConnectionString` | `features.redis=true` |
| `Kafka__BootstrapServers` | any `features.*=kafka` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | always (empty disables the Azure Monitor exporter) |

## Upgrading the chart

- **`helm upgrade`** triggers the migration Job (when Postgres) as a `pre-upgrade` hook. The Deployment rolls only after the Job succeeds.
- ConfigMap and Secret changes alone roll the Deployment thanks to the `checksum/config` + `checksum/secret` annotations on the pod template.
- **Never** run `--force` on a release in a shared environment — Helm recreates resources rather than patching, which drops the Deployment's revision history and can evict traffic.

## Developer reference: how the selector is wired into the Deployment

```
values.yaml          _helpers.tpl::validateFeatures
    │                           │  (render-time fail-fast)
    ▼                           ▼
configmap.yaml      ─────▶  Kubernetes ConfigMap
    │                   ┌──────────────────┐
    │                   │ Features__       │
    │                   │ InboundAdapter   │
    │                   │ OutboundAdapter  │
    │                   │ Persistence      │
    │                   │ UseRedis         │
    │                   └──────────────────┘
    │                           │
    ▼                           ▼ (envFrom)
deployment.yaml     ─────▶  Pod env
                                │
                                ▼ (at startup)
                      FeaturesOptions.Validate()
                                │
                                ▼
                      Conditional DI registration in
                      Configurations/ServiceConfigs.cs
```

The code path lives in [`src/Hex.Scaffold.Api/Configurations/ServiceConfigs.cs`](../../../src/Hex.Scaffold.Api/Configurations/ServiceConfigs.cs). Persistence adapters, Kafka producer, Kafka consumer + `BackgroundService` all register only when the flags say so.
