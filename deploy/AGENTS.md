<!-- Parent: ../AGENTS.md -->
<!-- Generated: 2026-05-02 | Updated: 2026-05-02 -->

# deploy

## Purpose
Deployment artifacts. Today there's a single Helm chart for AKS / generic Kubernetes deployment of the API + a sidecar WireMock for the outbound HTTP path. Migrations run as a pre-install / pre-upgrade Job.

## Subdirectories
| Directory | Purpose |
|-----------|---------|
| `helm/hex-scaffold/` | The Helm chart: `Chart.yaml`, `values.yaml`, `templates/`, `README.md` (per-value docs) |

## For AI Agents

### Working In This Directory
- `values.yaml` is the single source of truth for chart inputs. Each top-level key (`replicaCount`, `image`, `resources`, `wiremock`, `features`, `rateLimit`, `secrets`, `autoscaling`, `ingress`, etc.) is documented inline.
- Resources block: API container has `requests.cpu=100m / mem=256Mi`, `limits.cpu=1 / mem=512Mi` on master. The cluster has been running with a downsized variant — if you see latency spikes under load, check `kubectl get deploy hex-scaffold -o jsonpath='{.spec.template.spec.containers[0].resources}'` against the chart values.
- Memory quantities must use Kubernetes suffixes: `Ki / Mi / Gi / Ti` (binary) or `K / M / G / T` (decimal). A typo like `768i` (no `M`) renders fine but fails on `helm upgrade` with `unable to parse quantity's suffix`. There is no `i` suffix.
- `features.*` in values.yaml maps 1:1 onto the API's `FeaturesOptions`; flipping `Persistence: postgres` → `mongo` swaps the registered repository at startup.
- The Postgres connection string in `secrets.postgresConnectionString` is committed in plaintext for local/demo use only — production must use a real secret-management system (CSI driver, External Secrets, Sealed Secrets). Do not commit live credentials.

### Testing Requirements
- `helm lint deploy/helm/hex-scaffold` runs cleanly (one INFO about a missing icon is expected).
- Render check: `helm template hex-scaffold deploy/helm/hex-scaffold | kubectl apply --dry-run=client -f -` — catches malformed quantities before `helm upgrade` does.
- `helm upgrade --install hex-scaffold deploy/helm/hex-scaffold -n hex-scaffold` for a real deploy.

### Common Patterns
- Templates live in `templates/`: `deployment.yaml`, `hpa.yaml`, `ingress.yaml`, `migration-job.yaml`, `secret.yaml`, `service.yaml`, `wiremock-deployment.yaml`, `wiremock-mappings-configmap.yaml`, plus `_helpers.tpl`.
- Wiremock is a peer Deployment used to stub the outbound HTTP path under load test; its mappings are rendered from `wiremock.mappings` in `values.yaml`.
- Migration Job runs `dotnet ef database update` via the bundled efbundle and is hooked to `pre-install,pre-upgrade`.

## Dependencies

### External
- Helm 3.x
- Kubernetes 1.27+
- AKS (primary target; chart works on any conformant Kubernetes)

<!-- MANUAL: -->
