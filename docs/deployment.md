# Deployment Guide

> **Chart-local reference**: every `values.yaml` parameter is documented in full at [`deploy/helm/hex-scaffold/README.md`](../deploy/helm/hex-scaffold/README.md) — the canonical place for parameter types, defaults, allowed values, and override guidance. This page covers install flow and cross-cutting concerns only.

## Helm chart

Location: [`deploy/helm/hex-scaffold`](../deploy/helm/hex-scaffold) · Reference: [`deploy/helm/hex-scaffold/README.md`](../deploy/helm/hex-scaffold/README.md).

### Install

```bash
helm upgrade --install hex-scaffold ./deploy/helm/hex-scaffold \
  --namespace default --create-namespace \
  --set features.inbound=rest \
  --set features.outbound=kafka \
  --set features.persistence=postgres \
  --set features.redis=true \
  --set image.tag=0.1.0 \
  --set secrets.postgresConnectionString="Host=pg;Database=hex;Username=app;Password=...;Port=5432" \
  --set secrets.kafkaBootstrapServers="my-cluster-kafka-bootstrap.kafka.svc:9092" \
  --set secrets.appInsightsConnectionString="$APP_INSIGHTS_CS"
```

### The single source of truth: the ConfigMap

The template [`configmap.yaml`](../deploy/helm/hex-scaffold/templates/configmap.yaml) renders a ConfigMap that is **the** knob for adapter selection. Its `data:` block mirrors `.NET` configuration keys with the usual double-underscore delimiter:

```yaml
Features__InboundAdapter:  "rest"     # rest | kafka
Features__OutboundAdapter: "kafka"    # rest | kafka
Features__Persistence:     "postgres" # postgres | mongo
Features__UseRedis:        "true"     # only valid with postgres
```

The Deployment mounts this ConfigMap (and the Secret with connection strings) via `envFrom`. At startup, [`FeaturesOptions.Validate()`](../src/Hex.Scaffold.Api/Options/FeaturesOptions.cs) enforces:

1. Exactly **one** inbound adapter (`rest` **or** `kafka`).
2. Exactly **one** outbound adapter (`rest` **or** `kafka`).
3. Exactly **one** primary store (`postgres` **or** `mongo`).
4. `UseRedis=true` is only valid with `persistence=postgres`.

Nonsensical combinations fail the pod fast with a readable error.

### What the chart renders

| Template | When rendered |
|----------|---------------|
| `configmap.yaml` | always |
| `secret.yaml` | always — keys inside conditional on features |
| `deployment.yaml` | always |
| `service.yaml` | when `features.inbound=rest` |
| `ingress.yaml` | when `features.inbound=rest` AND `ingress.enabled=true` |
| `hpa.yaml` | when `autoscaling.enabled=true` |
| `migration-job.yaml` | when `features.persistence=postgres` AND `migrations.enabled=true` — pre-install / pre-upgrade Helm hook (weight `-5`); invokes the bundled `/app/efbundle` shipped inside the runtime image (see [`docs/database.md`](database.md)) |
| `wiremock-deployment.yaml` · `wiremock-service.yaml` · `wiremock-mappings-configmap.yaml` | when `wiremock.enabled=true` (default) — in-cluster mock that the outbound HTTP adapter calls via `ExternalApi__BaseUrl`. The chart helper rewrites the base URL automatically; `wiremock.fixedDelayMs` (default 300ms) is injected into every stub's response. |

`configmap.yaml` and `secret.yaml` carry `pre-install,pre-upgrade` hook annotations at weight `-10` so they exist before the migration Job at weight `-5` (the Job `envFrom`s them). `before-hook-creation` keeps them in the cluster after the hook completes; `helm uninstall` still removes them.

### Validating a release without hitting the cluster

```bash
helm lint ./deploy/helm/hex-scaffold
helm template hex-scaffold ./deploy/helm/hex-scaffold \
  --set features.inbound=kafka --set features.outbound=rest \
  --set features.persistence=mongo --set features.redis=false \
  | kubectl apply --dry-run=client -f -
```

## Image

See [`Dockerfile`](../Dockerfile) (BuildKit, multi-arch, chiseled runtime, non-root). The runtime image carries two binaries:

- `Hex.Scaffold.Api.dll` — the application (default `ENTRYPOINT`).
- `/app/efbundle` — a self-contained EF Core migration bundle invoked by the Helm pre-install/pre-upgrade Job. See [`docs/database.md`](database.md#migration-delivery-efbundle-built-into-the-runtime-image).

The `release` GitHub Actions workflow publishes to `ghcr.io/lurodrisilva/net-hexagonal` on every push to `master` and on tags (`v*.*.*`). Tag matrix per event:

| Event | Tags applied |
|---|---|
| Push to `master`/`main` | `master`/`main`, `edge`, `sha-<short>`, **`latest`** |
| Tag `v1.2.3` | `1.2.3`, `1.2`, `1`, `sha-<short>`, **`latest`** |
| Pull request | builds only — never pushes |

Because `:latest` is mutable, the chart defaults `image.pullPolicy=Always` and `migrations.image.pullPolicy=Always` so AKS nodes re-pull on every rollout. Override to `IfNotPresent` when pinning to immutable semver/sha tags.

## Secrets in production

The chart ships a Secret template **for demo use only**. Replace it with one of:

- Azure Key Vault CSI driver + `SecretProviderClass`
- External Secrets Operator (`ExternalSecret` / `ClusterSecretStore`)
- Sealed Secrets (`kubeseal`)

All the app needs is these keys visible as env vars:

| Env var | Populated when |
|---------|----------------|
| `ConnectionStrings__PostgreSql` | `persistence=postgres` |
| `MongoDB__ConnectionString` | `persistence=mongo` |
| `Redis__ConnectionString` | `redis=true` |
| `Kafka__BootstrapServers` | any `*=kafka` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | always (empty disables Azure Monitor) |
