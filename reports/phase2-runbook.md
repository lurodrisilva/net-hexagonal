# Phase 2 Runbook — Kafka Loadtest Observability Provisioning

Plan reference: `.omc/plans/kafka-loadtest-plan.md` §4 (revision 4).
Branch: `feat/kafka-loadtest-phase2-observability`.
Date opened: 2026-05-05.

This runbook captures every Azure CLI command run during Phase 2, its
output, the resulting decisions, and the deferred / manual operator steps
that Phase 5 (execution) needs. Treat it as the audit trail an operator
follows to take the system from "Phase 1 merged" to "ready for Phase 5".

---

## §4.2.1 — Application Insights resource verification + daily cap decision

### Context (from PRD)

| Key | Value |
|---|---|
| Subscription | `df21ed78-be77-40e3-9184-38eb23175791` |
| Resource group | `resources-test-rg` |
| AI resource name | `app-ins-test` |
| Region | `brazilsouth` |

### Step 1 — Verify the AI resource exists and capture its state

```bash
az monitor app-insights component show \
  --app app-ins-test \
  --resource-group resources-test-rg \
  --subscription df21ed78-be77-40e3-9184-38eb23175791
```

**Captured (2026-05-05):**

| Field | Value |
|---|---|
| `applicationId` | `e65c9fd9-b510-48e7-894f-32f69a230d6d` |
| `instrumentationKey` | `84057efc-9957-45a5-b19b-5c60cd12890e` |
| `connectionString` | `InstrumentationKey=84057efc-9957-45a5-b19b-5c60cd12890e;IngestionEndpoint=https://brazilsouth-1.in.applicationinsights.azure.com/;LiveEndpoint=https://brazilsouth.livediagnostics.monitor.azure.com/;ApplicationId=e65c9fd9-b510-48e7-894f-32f69a230d6d` |
| `location` | `brazilsouth` |
| `kind` | `web` |
| `applicationType` | `web` |
| `retentionInDays` | `90` |
| `workspaceResourceId` | `/subscriptions/df21ed78-…/resourceGroups/DefaultResourceGroup-CQ/providers/Microsoft.OperationalInsights/workspaces/DefaultWorkspace-CQ` |

**Verdict:** workspace-based AI confirmed (`workspaceResourceId` non-null;
ingestion lands in Log Analytics workspace `DefaultWorkspace-CQ`).
Connection string matches the value already pinned in
`deploy/helm/hex-scaffold/values.yaml` line 61.

### Step 2 — Read current daily ingestion cap

The `az monitor app-insights component get-quota` subcommand was removed in
recent CLI versions; the `az rest` GET below queries the underlying
`CurrentBillingFeatures` endpoint directly.

```bash
az rest --method GET \
  --uri "https://management.azure.com/subscriptions/df21ed78-be77-40e3-9184-38eb23175791/resourceGroups/resources-test-rg/providers/Microsoft.Insights/components/app-ins-test/CurrentBillingFeatures?api-version=2015-05-01"
```

**Captured (2026-05-05):**

| Field | Value |
|---|---|
| `DataVolumeCap.Cap` | `100.0` GB / day |
| `DataVolumeCap.WarningThreshold` | `90` % |
| `DataVolumeCap.ResetTime` | `19` (hour UTC) |
| `DataVolumeCap.MaxHistoryCap` | `100.0` |

### Step 3 — Decision: leave cap at 100 GB/day for the loadtest cycle

Plan §4.2.1 specifies a 20 GB cap. **Decision: leave cap at 100 GB.**

**Rationale:**

1. Predicted ingest across all 6 runs (with §4.2.4 sampling 0.1 + log root
   `Warning` + outbound suppression via §3.6) is **~10–15 GB**. The current
   100 GB cap is already ~7× the predicted ceiling — protective as-is.
2. Lowering to 20 GB introduces tail risk: the critic flagged in plan
   rev 4 that the sampling math could under-estimate. A mid-run cap trip
   would silently drop telemetry and invalidate the affected run's
   stop-condition data.
3. The primary cost controls (sampling ratio, log level, outbound
   suppression) ship in US-002/US-003. The cap is a backstop, not the
   first line of defence. 100 GB is a healthy backstop.
4. No `az ... component update --cap` mutation was performed.

**Revisit trigger:** if Phase 5 ingestion crosses 50 GB on any single
loadtest day, lower to 20 GB before subsequent runs and document here.

### Post-cycle reset (after Phase 6 reporting completes)

No reset needed for the cap (left at 100 GB). The classic-SDK adaptive
sampling and OTel trace ratio knobs added in US-003 / US-004 remain in
the chart — they default to permissive values
(`tracesRatio=1.0`, `maxTelemetryItemsPerSecond=5`) so production
behaviour is unchanged. The loadtest values overlay
(`values-aks-test-loadtest.yaml`, US-002) is the only artifact carrying
restrictive settings, and it is only applied via `helm upgrade -f` for
loadtest installs.

### Deferred / manual operator steps

None for §4.2.1. The cap decision is final for this cycle.

---

## §4.2.3 — Network egress verification

The consumer pods + k6 runners must reach all of the endpoints below. AI
region resolved from the captured connection string (§4.2.1) is
**`brazilsouth`** — not the `eastus2` placeholder shown in plan §4.2.3.

| # | Endpoint | Port | Purpose | Verified now? |
|---|---|---|---|---|
| 1 | `brazilsouth-1.in.applicationinsights.azure.com` | 443 | OTel + classic-SDK trace / metric / log ingestion | ✅ live (this runbook) |
| 2 | `brazilsouth.livediagnostics.monitor.azure.com` | 443 | Live Metrics (QuickPulse) | ✅ live (this runbook) |
| 3 | `dc.services.visualstudio.com` | 443 | Classic SDK fallback ingestion | ✅ live (this runbook) |
| 4 | `<aks-test PG hostname>` | 5432 | PostgreSQL (per-tier) | ⏸️ deferred to Phase 5 (needs secret) |
| 5 | `<DocumentDB account>.mongocluster.cosmos.azure.com` | 10260 | DocumentDB (Cosmos vCore) | ⏸️ deferred to Phase 5 (needs secret) |
| 6 | `kafka-bootstrap.kafka.svc` | 9092 | In-cluster Kafka (no egress; verify NetworkPolicy) | ⏸️ deferred to Phase 5 (needs cluster state) |

> **Operator note:** the PG hostname and DocumentDB account name carry
> connection-string-grade sensitivity. Fill them in at Phase 5 pre-flight
> from the K8s secrets — never commit them to this runbook.

### Live verification — AI endpoints (executed during US-006)

Run from a `nodepool3` pod with auto-cleanup:

```bash
kubectl run egress-check-phase2 --rm -i --restart=Never \
  --image=curlimages/curl:8.10.1 \
  --overrides='{"spec":{"nodeSelector":{"agentpool":"nodepool3"}}}' -- sh -c '
echo "=== AI ingestion (brazilsouth-1) ==="
curl -sI -o /dev/null -w "HTTP %{http_code}\n" --max-time 10 \
  https://brazilsouth-1.in.applicationinsights.azure.com/v2.1/track
echo "=== Live Metrics QuickPulse (brazilsouth) ==="
curl -sI -o /dev/null -w "HTTP %{http_code}\n" --max-time 10 \
  https://brazilsouth.livediagnostics.monitor.azure.com/QuickPulseService.svc/ping
echo "=== Classic SDK fallback (dc.services.visualstudio.com) ==="
curl -sI -o /dev/null -w "HTTP %{http_code}\n" --max-time 10 \
  https://dc.services.visualstudio.com/v2/track
'
```

**Captured (2026-05-05, from `aks-nodepool3-15160898-vmss00000X`):**

| Endpoint | Status | Interpretation |
|---|---|---|
| `brazilsouth-1.in.applicationinsights.azure.com/v2.1/track` | `HTTP 405` | Reachable — server rejects HEAD on POST-only ingest URI. Expected. |
| `brazilsouth.livediagnostics.monitor.azure.com/QuickPulseService.svc/ping` | `HTTP 400` | Reachable — server rejects ping without QuickPulse handshake headers. Expected. |
| `dc.services.visualstudio.com/v2/track` | `HTTP 405` | Reachable — same POST-only behaviour as #1. Expected. |

All three endpoints respond with an Azure-issued HTTP status code, which
confirms TLS handshake + DNS + egress routing all the way through. Pod
auto-deleted by `--rm`.

### Live verification — PG / DocumentDB / Kafka (deferred to Phase 5)

Operator commands to run during Phase 5 pre-flight (after sourcing the
`PG_HOST` / `DOCDB_HOST` shell variables from secrets):

```bash
kubectl run egress-check-phase5 --rm -i --restart=Never \
  --image=curlimages/curl:8.10.1 \
  --overrides='{"spec":{"nodeSelector":{"agentpool":"nodepool3"}}}' -- sh -c "
nc -zv ${PG_HOST} 5432
nc -zv ${DOCDB_HOST} 10260
nc -zv kafka-bootstrap.kafka.svc 9092
"
```

Any failure blocks the affected run.

## §4.4 — Pre-execution validation gate

Operator gate to run **before** Phase 5 starts. Each row maps to a plan
§4.4 checklist item, classified as:

- **Auto-now** — verifiable now (Phase 2 artefacts already exist).
- **Phase 5** — requires live cluster state at run time (synthetic event,
  AI Live Metrics observability, post-message check).

| # | Check | Phase | Command / Evidence |
|---|---|---|---|
| 1 | AI resource exists; connection string captured; not the demo key | Auto-now | See §4.2.1. Conn string verified non-empty + matches values.yaml line 61. |
| 2 | AI daily cap set | Auto-now | Cap **left at 100 GB** (decision recorded §4.2.1). Predicted ingest 10–15 GB stays well under. |
| 3 | `values-aks-test-loadtest.yaml` overlay exists; `helm template` renders cleanly | Auto-now | Run command (a) below. |
| 4 | Egress check from `nodepool3` pod returns success from AI endpoints | Auto-now | See §4.2.3 live verification block above. |
| 5 | Egress check from `nodepool3` pod returns success from PG / DocumentDB / Kafka | Phase 5 | See §4.2.3 deferred block. Needs `PG_HOST` / `DOCDB_HOST` from secrets. |
| 6 | Sampling block verified — startup logs reflect ratio + classic-SDK init | Phase 5 | Tail consumer pod logs after `helm upgrade` with overlay; look for `OpenTelemetry.Trace.TracerProviderBuilder` + `Microsoft.ApplicationInsights` initialisation lines. |
| 7 | Mongo diagnostic source subscribed; Performance pane shows MongoDB row | Phase 5 | After 1 Mongo write, AI Portal → Performance → Dependencies tab → MongoDB row appears within 60 s. |
| 8 | Histogram exported tag set verified — only `event_type`, `tier`, `repo` | Phase 5 | Run command (b) below from inside a consumer pod. |
| 9 | Live Metrics Stream open — synthetic event appears as request + dependency | Phase 5 | Open AI Portal → Live Metrics; emit one Kafka inbound event; observe HTTP request count tick + Postgres / Mongo dependency tick within 5 s. |
| 10 | AI Failures pane shows zero failures in last 10 min (clean baseline) | Phase 5 | AI Portal → Failures, time range last 10 min, expect empty grid. |

### Operator commands

**(a) Helm overlay render check (Auto-now):**

```bash
rtk proxy 'helm template hex-scaffold deploy/helm/hex-scaffold \
  -f deploy/helm/hex-scaffold/values-aks-test-loadtest.yaml \
  --set-string secrets.appInsightsConnectionString="placeholder-conn-string" \
  -s templates/configmap.yaml' \
  | grep -E "Observability__Sampling__TracesRatio|ClassicSdk__MaxTelemetryItemsPerSecond|Serilog__MinimumLevel|Features__InboundAdapter|Kafka__InboundTopic"
```

Expected output contains:
- `Observability__Sampling__TracesRatio: "0.1"`
- `ClassicSdk__MaxTelemetryItemsPerSecond: "50"`
- `Serilog__MinimumLevel__Default: "Warning"`
- `Serilog__MinimumLevel__Override__Hex.Scaffold.Adapters.Inbound.Messaging.AccountEventConsumer: "Information"`
- `Features__InboundAdapter: "kafka"`

**(b) Histogram cardinality check (Phase 5, after pod up):**

```bash
kubectl exec -n default deploy/hex-scaffold -- \
  curl -s http://localhost:8080/metrics | \
  grep inbound_event_processing_duration_ms | head -10
```

Expected: every series shows only `event_type`, `tier`, `repo` label keys
(no `cloud_RoleInstance`, no `service.instance.id`, no other tag).

---

## Phase 2 → Phase 3 handoff

Phase 3 (Strimzi topology + Helm KafkaTopic CRs + Grafana dashboard) must
consume the artefacts below from Phase 2. Phase 4 (k6 script) and Phase 5
(execution) consume the same artefacts plus the deferred-to-Phase-5
checks from the table above.

| Artefact | Path / Command | Consumer |
|---|---|---|
| Helm overlay (loadtest knobs) | `deploy/helm/hex-scaffold/values-aks-test-loadtest.yaml` | Phase 3 (combine with per-tier overlay), Phase 5 install |
| AI connection string fetch | `az monitor app-insights component show --app app-ins-test --resource-group resources-test-rg --subscription df21ed78-be77-40e3-9184-38eb23175791 --query connectionString -o tsv` | Phase 5 install (`--set-string secrets.appInsightsConnectionString=$AI_CONN`) |
| AI region (for egress checks) | `brazilsouth` (already baked into §4.2.3) | Phase 5 pre-flight |
| Predicted ingest budget | ~10–15 GB across 6 runs (cap at 100 GB) | Phase 5 monitoring trigger (lower cap if any single day > 50 GB) |
| Inbound topic naming convention | `v2.core.accounts.loadtest.<tier>.<short-repo>` | Phase 3 KafkaTopic CRs, Phase 5 install (`--set kafka.inboundTopic=...`) |
| Cardinality contract | OTel View pins `inbound_event_processing_duration_ms` to `{event_type, tier, repo}` | Phase 4 k6 producer headers must populate `tier` + `repo` to keep series labelled |
