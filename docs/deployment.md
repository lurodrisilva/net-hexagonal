# Deployment Guide

## Helm chart

Location: [`deploy/helm/hex-scaffold`](../deploy/helm/hex-scaffold).

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
| `migration-job.yaml` | when `features.persistence=postgres` AND `migrations.enabled=true` — pre-install / pre-upgrade Helm hook |

### Validating a release without hitting the cluster

```bash
helm lint ./deploy/helm/hex-scaffold
helm template hex-scaffold ./deploy/helm/hex-scaffold \
  --set features.inbound=kafka --set features.outbound=rest \
  --set features.persistence=mongo --set features.redis=false \
  | kubectl apply --dry-run=client -f -
```

## Image

See [`Dockerfile`](../Dockerfile) (BuildKit, multi-arch, chiseled runtime, non-root). The `release` GitHub Actions workflow publishes `ghcr.io/lurodrisilva/net-hexagonal:<sha|tag>` on every push to `master` and on tags (`v*.*.*`).

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
