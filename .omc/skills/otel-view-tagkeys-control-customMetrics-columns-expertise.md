---
name: otel-view-tagkeys-control-customMetrics-columns
description: OTel MetricStreamConfiguration.TagKeys is the only thing that propagates a dimension into Application Insights customMetrics — Activity tags and Resource attributes do not become filterable columns
triggers:
  - customMetrics filter returns empty
  - missing tier or repo in App Insights
  - inbound_event_processing_duration_ms p95 query
  - cross-repo or cross-tier comparison
  - AddView TagKeys
  - MetricStreamConfiguration
  - "where customDimensions.repo == 'mongo'"
  - cardinality explosion cloud_RoleInstance
  - re-run because metric tag missing
---

# OTel View TagKeys Control AppInsights customMetrics Columns

## The Insight

In OpenTelemetry .NET + Azure Monitor exporter, the `MetricStreamConfiguration.TagKeys` set on `AddView(...)` is the **only** mechanism that decides which dimensions land in Application Insights `customMetrics.customDimensions`. Activity tags (`Activity.Current?.SetTag(...)`), Resource attributes, and ambient `IMeterFactory` baggage do **not** propagate.

If a dimension is missing from `TagKeys`, it is **dropped at the meter aggregation boundary** — by the time the Azure Monitor exporter ships the histogram, the tag is gone. No KQL query, no Performance pane filter, and no `summarize by` will recover it.

## Why This Matters

Cross-repo / cross-tier comparison runs share a single Application Insights resource. The discriminator (e.g. `repo='mongo'` vs `repo='pg'`) MUST be a metric tag, not an Activity tag, or the run is unrecoverable post-hoc.

Concrete burn from this repo: Phase 5 silver-pg run on 2026-05-06 set `repo` only via `Activity.Current.SetTag("repo", ...)`. The histogram landed in `customMetrics` with `event_type` and `tier` columns but no `repo` column — every query that filtered `customDimensions.repo == 'pg'` returned zero rows. PR #60 added `"repo"` to the View's `TagKeys` array; the next run had the column. The lost run was unsalvageable — no amount of Activity-side correlation could reconstruct the missing aggregation key.

The bug is silent. The metric still appears, the count still looks plausible, p95 still has a value — but it is the **aggregate across all repos**, not the per-repo slice. Without a sentinel filter check before run start, a corrupted run looks identical to a clean run.

## Recognition Pattern

- Cross-cutting comparison runs share an AI resource: tier × repo × runId, A/B tests, customer-vs-control.
- KQL: `customMetrics | where name == "<metric>" | where customDimensions.<key> == "<value>"` returns 0 rows where you expected hundreds.
- Performance pane "Split by" dropdown does not list the dimension you tagged.
- An `Activity.SetTag(key, value)` exists in code but the dimension is not in any `MetricStreamConfiguration.TagKeys`.
- `inbound_event_processing_duration_ms` in this repo: only `event_type, tier, repo` survive — anything else is stripped by `ObservabilityConfig.cs:144-149`.

## The Approach

Before any production / load-test run that needs to filter a metric by dimension X:

1. **Locate the View.** In this repo: `src/Hex.Scaffold.Api/Configurations/ObservabilityConfig.cs:144-149`. Confirm X is in `TagKeys`.
2. **If X is missing, add it BEFORE the run, not after.** Adding TagKeys post-hoc cannot recover lost runs.
3. **Verify with a smoke before the real run.** Send 10 events tagged with X, query `customMetrics | summarize count() by tostring(customDimensions.<X>)`. Must show non-empty `<X>`. If empty: the View is wrong, the meter name is wrong, or the tag is being set on Activity not on the histogram `Record(value, KeyValuePair<...>...)` call.
4. **Cardinality discipline.** Each TagKey multiplies series count. Bound it: `event_type` (4 values), `tier` (3), `repo` (2) = 24 series per metric. Adding `runId` would multiply by run count → series explosion → AI ingest cap → throttling. Keep run identity in `customDimensions` via Activity / Resource — only when post-hoc filtering by it is acceptable to be a derived correlation, not a primary slicer.
5. **Mental model:** `TagKeys` is a whitelist. The aggregator strips everything else. Activity tags ride alongside trace span data, not metric data.

## Project-Specific Anchors

- View definition: `src/Hex.Scaffold.Api/Configurations/ObservabilityConfig.cs:144-149`
- Allowed TagKeys: `event_type`, `tier`, `repo`
- Histogram name: `inbound_event_processing_duration_ms`
- Meter source: `Hex.Scaffold.Adapters.Inbound.Messaging`
- AI resource: see `appInsightsConnectionString` wiring in same file (line ~153)
- Hard-won fix: PR #60 added `"repo"` after silver-pg 2026-05-06 run was lost

## Anti-Pattern

```csharp
// Set on the Activity span — DOES NOT become a customMetrics column.
Activity.Current?.SetTag("repo", "mongo");
histogram.Record(elapsedMs, new KeyValuePair<string, object?>("event_type", "created"));
```

## Correct Pattern

```csharp
// Tag on the Record() call — flows through MeterProvider → View.TagKeys → exporter.
histogram.Record(
  elapsedMs,
  new KeyValuePair<string, object?>("event_type", "created"),
  new KeyValuePair<string, object?>("tier", tier),
  new KeyValuePair<string, object?>("repo", repo));
```

And ensure `"repo"` is in the View:

```csharp
.AddView(
  instrumentName: "inbound_event_processing_duration_ms",
  new MetricStreamConfiguration
  {
    TagKeys = new[] { "event_type", "tier", "repo" },
  });
```
