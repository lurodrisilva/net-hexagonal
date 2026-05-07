---
name: azure-retail-prices
description: Azure Retail Prices REST API (https://prices.azure.com/api/retail/prices) is anonymous, commercial-cloud-only, and a *catalog* — not a billing source. $filter is case-sensitive on api-version=2023-01-01-preview, OData supports only eq/and/contains, savingsPlan rates require the preview version, USD is the only currency Microsoft bills in, and pagination requires walking NextPageLink to null.
triggers:
  - prices.azure.com
  - api/retail/prices
  - azure retail prices
  - azure pricing api
  - azure cost api
  - azure price comparison
  - vm price by region
  - savings plan rates
  - reservation pricing
  - spot price azure
  - azure currency conversion
  - service family pricing
  - programmatic azure pricing
  - track azure price changes
  - FinOps azure
  - retail prices rest api
  - $filter serviceName
  - $filter armRegionName
  - currencyCode='EUR'
  - meterRegion='primary'
  - api-version=2023-01-01-preview
  - NextPageLink
---

# Azure Retail Prices API — Distinguished Azure Platform Engineer's Playbook

You are a **distinguished Azure Platform Engineer** integrating the **Azure Retail Prices REST API** into a tool, dashboard, FinOps workflow, or budget-modeling pipeline. Your job is to ship a caller that returns **defensible, reproducible** prices — pinned to a specific API version, paginated to completion, currency-disambiguated, version-stamped, and **never** silently mistaken for invoiced billing.

This skill encodes the **API contract** (endpoint, versions, filters, response schema, pagination, currency semantics) and the **operational discipline** that turns a one-off `curl` into a production caller (caching, backoff, drift detection, enum auditing, USD-vs-reference separation).

## The Insight

The Azure Retail Prices API looks simple — anonymous `GET`, JSON response, an OData-flavoured `$filter` parameter. In practice it has **twelve** non-obvious rules that silently corrupt downstream cost models when violated:

1. **Anonymous and commercial-cloud only.** No `Authorization` header. Government / China / Germany sovereign clouds are out of scope.
2. **Pin `?api-version=2023-01-01-preview`** when you need `savingsPlan[]` or `meterRegion='primary'`. Default URL works but omits both. A caller that omits the version then accesses `item.savingsPlan` will see `undefined` on every row and silently miscalculate.
3. **`$filter` values are case-sensitive on `2023-01-01-preview` and later.** `'Virtual Machines'` works, `'virtual machines'` returns 0 rows + HTTP 200 (no error). Same trap on `serviceFamily`, `priceType`, every string field.
4. **Always paginate to `NextPageLink === null`.** Page size is 1,000 rows. Any non-trivial filter returns 100+ pages. Capping at first page is the single most common production bug — silently undercounts SKUs without any error signal.
5. **USD is the only currency Microsoft bills in.** `currencyCode='EUR'` returns prices, but those are **estimates for budgeting**. Microsoft converts USD→local-currency at invoice time using their own FX rate, which will not match the API. Never reconcile a non-USD `retailPrice` against an Azure invoice.
6. **OData filter surface is small: `eq`, `and`, `contains(...)`.** No `or` / `ne` / `gt` / `lt` / `startswith`. Logical OR ⇒ two separate calls + client merge. Exclusion ⇒ inclusion call + client filter.
7. **No documented rate limits ⇒ assume conservative.** Microsoft does not publish an SLA, retry-after, or rate cap for `prices.azure.com`. Production callers must cache (≥24 h TTL keyed on `(filter, currencyCode, api-version)`), backoff exponentially with jitter on 429/5xx, never retry 4xx.
8. **`isPrimaryMeterRegion: false` rows are nonprimary meters — usually skip.** The default URL returns *all* meters including nonprimary. For "what does this SKU cost" reports, filter to `isPrimaryMeterRegion === true` client-side, OR add `&meterRegion='primary'` server-side (preview API only). Mixing primary + nonprimary in an aggregate double-counts.
9. **`Reservation.retailPrice` is the *total* for the term, not per-hour.** `type='Reservation'`, `reservationTerm='1 Year'`, `retailPrice=25007.0`, `unitOfMeasure='1 Hour'` does **not** mean $25,007/hr. It means $25,007 for 8,760 hours. To compare to Consumption: `25007 / 8760 = $2.855/hr`. Three-year divides by 26,280.
10. **`priceType=DevTestConsumption` rows are MSDN/Visual Studio rates.** Mixing them with `Consumption` for the same SKU shows a 50% discount that doesn't exist on Enterprise Agreement. Filter `$filter=priceType eq 'Consumption'` for production cost models.
11. **Treat `serviceName` / `serviceFamily` / `meterName` as a slowly-drifting enum.** Microsoft renames products ("Cosmos DB" → "Azure Cosmos DB" → split lines). A hard-coded `serviceName` filter silently returns zero rows the day they rename. Defenses: monthly enum-snapshot diff, prefer `serviceFamily` (26 values, more stable) over `serviceName` (~200 values, drifting), alert on filters that suddenly return 0 rows.
12. **The API is a *catalog*, not a *billing* source.** Use for budget modeling, region comparison, savings-plan TCO, reservation breakeven, public pricing pages, FinOps "what-if". Do **not** use for invoice reconciliation, showback / chargeback (use Cost Management Exports + Usage Details), or audit-grade reporting (use EA / MCA Billing API).

If a caller, dashboard, or pipeline under review violates any of these, **flag them first** before any other comment.

## Why This Matters

Hex.Scaffold's load-testing harness already runs against Azure-managed services (PG Flex, DocumentDB Mongo vCore, AKS, Strimzi). When the next FinOps or capacity-planning task asks "what would this Platinum tier cost in West Europe?" or "compare Mongo gold vs PG gold across regions," the temptation is to write a quick `curl` against `prices.azure.com`. That quick `curl` then becomes a dashboard, then a CI-gated cost-model script, and the silent failures (unpaginated, lowercase filter, mixed primary/nonprimary, Reservation-as-hourly) ship into an architecture review where the numbers tilt the decision.

This skill exists so the *first* call against this API is paginated, version-pinned, case-correct, USD-anchored, and labeled correctly for what it is (a catalog snapshot, not a billing oracle).

## Recognition Pattern

This skill applies when:

- A code path imports / hard-codes the URL `https://prices.azure.com/api/retail/prices`.
- A KQL / dashboard / spreadsheet contains hand-built Azure prices that need a programmatic refresh.
- A FinOps task asks for "current Azure prices" or "compare regions" or "model a reservation."
- A reviewer sees an HTTP request to `prices.azure.com` without a paginating loop.
- A reviewer sees an `Authorization` header attached to a `prices.azure.com` request (incorrect — the endpoint is anonymous).
- A SKU-vs-region cost matrix references `retailPrice` of `priceType=Reservation` rows alongside `Consumption` rows without dividing by term hours.
- A budget alert in a non-USD currency is being reconciled against an Azure invoice line.
- A `$filter=serviceName eq 'virtual machines'` pattern (lowercase) appears in code or notebook.

## The Approach

### API contract — one-page reference

**Endpoint:** `GET https://prices.azure.com/api/retail/prices` (anonymous; Commercial Cloud only).

**API versions:**

| Version | Behavior | When |
|---|---|---|
| *(omitted)* | Stable. Full meter set, no `savingsPlan`, no `meterRegion`. | Legacy callers only. |
| `?api-version=2023-01-01-preview` | Adds `savingsPlan[]`, `meterRegion='primary'`. **Filter values become case-sensitive.** | **Default for new callers.** |

**Query parameters:** `api-version`, `$filter`, `currencyCode` (e.g. `'EUR'` — quotes required), `meterRegion='primary'` (preview only), `$skip` (do not set manually; emitted in `NextPageLink`).

**`$filter` fields:** `armRegionName`, `Location`, `meterId`, `meterName`, `productId`, `skuId`, `productName`, `skuName`, `serviceName`, `serviceId`, `serviceFamily`, `priceType`, `armSkuName`.

**`$filter` operators:** `eq`, `and`, `contains(field, 'substr')`. **Not supported:** `or`, `ne`, `gt`, `lt`, `startswith`, `endswith`.

**`priceType` enum:** `Consumption` (production default), `DevTestConsumption` (MSDN/VS subscriber rate — exclude from production), `Reservation` (1-yr / 3-yr; `retailPrice` is **total for the term**).

**`serviceFamily` enum (26 values, verbatim from MS docs 2026-01-06):**

```
Analytics, Azure Arc, Azure Communication Services, Azure Security, Azure Stack,
Compute, Containers, Data, Databases, Developer Tools, Dynamics, Gaming,
Integration, Internet of Things, Management and Governance, Microsoft Syntex,
Mixed Reality, Networking, Other, Power Platform, Quantum Computing, Security,
Storage, Telecommunications, Web, Windows Virtual Desktop
```

Audit monthly; Microsoft does add families.

**Response envelope:**

```json
{
  "BillingCurrency": "USD",
  "CustomerEntityId": "Default",
  "CustomerEntityType": "Retail",
  "Items": [ /* up to 1000 rows */ ],
  "NextPageLink": "https://prices.azure.com:443/api/retail/prices?$filter=...&$skip=1000",
  "Count": 1000
}
```

`NextPageLink` is `null`/absent on final page. Always check; do not assume `Count < 1000` is terminal.

**`Items[]` row schema:**

| Field | Notes |
|---|---|
| `currencyCode` | `'USD'` unless `currencyCode` query param was set. |
| `tierMinimumUnits` | Min units for this tier (often `0.0`). |
| `retailPrice` / `unitPrice` | Microsoft retail price, no discount. Equal in current API. |
| `armRegionName` | Slug, e.g. `'eastus'`, `'westeurope'`. |
| `location` | Display label, e.g. `'US East'`. |
| `effectiveStartDate` | ISO 8601. |
| `meterId`, `meterName`, `productId`, `productName`, `skuId`, `skuName`, `serviceId`, `serviceName`, `serviceFamily` | Catalog identifiers. |
| `unitOfMeasure` | e.g. `'1 Hour'`, `'1 GB/Month'`, `'10K Operations'`. |
| `type` | `'Consumption'` / `'DevTestConsumption'` / `'Reservation'`. |
| `isPrimaryMeterRegion` | `true` for the meter Microsoft uses to bill. |
| `armSkuName` | e.g. `'Standard_F16s'`. The ARM SKU you'd put in Bicep / Terraform. |
| `reservationTerm` | Only when `type='Reservation'`: `'1 Year'` / `'3 Years'`. |
| `savingsPlan` | Only on `2023-01-01-preview` API. `[{ unitPrice, retailPrice, term }]`. |

**Pagination contract:**

```
1. GET first URL.
2. Read response.Items[].
3. If response.NextPageLink is null/absent → stop.
4. Else GET response.NextPageLink verbatim. Goto 2.
```

Never reconstruct page URLs by hand — server may add cursor tokens beyond `$skip`.

### Reference calls (always pin `?api-version=2023-01-01-preview`)

**Single SKU in a region (smallest useful query):**
```
GET .../api/retail/prices?api-version=2023-01-01-preview
  &$filter=armRegionName eq 'eastus' and armSkuName eq 'Standard_D4s_v5' and priceType eq 'Consumption'
```

**All VMs in a region, primary meters only:**
```
GET .../api/retail/prices?api-version=2023-01-01-preview
  &$filter=serviceName eq 'Virtual Machines' and armRegionName eq 'westeurope' and priceType eq 'Consumption'
  &meterRegion='primary'
```

**All reservations for a SKU (1-yr + 3-yr):**
```
GET .../api/retail/prices?api-version=2023-01-01-preview
  &$filter=armSkuName eq 'Standard_E64_v4' and priceType eq 'Reservation'
```
Compute hourly: `retailPrice / (8760 for 1Y, 26280 for 3Y)`.

**Savings-plan eligible compute meters in a region:**
```
GET .../api/retail/prices?api-version=2023-01-01-preview
  &$filter=serviceFamily eq 'Compute' and priceType eq 'Consumption'
  &meterRegion='primary'
```
Filter client-side to rows where `item.savingsPlan && item.savingsPlan.length > 0`.

**EUR reference (mark display as "reference rate; Microsoft bills in USD"):**
```
GET .../api/retail/prices?api-version=2023-01-01-preview
  &currencyCode='EUR'
  &$filter=serviceFamily eq 'Compute' and armRegionName eq 'westeurope'
```

**Cosmos DB across regions (canonical region-comparison):**
```
GET .../api/retail/prices?api-version=2023-01-01-preview
  &$filter=serviceName eq 'Azure Cosmos DB' and priceType eq 'Consumption'
  &meterRegion='primary'
```
Group rows client-side by `armRegionName + meterName`. Watch slow drift on `serviceName`.

**Logical OR via two calls + client merge:**
```
# Call 1
GET .../api/retail/prices?$filter=armRegionName eq 'eastus2' and armSkuName eq 'Standard_D4s_v5'
# Call 2
GET .../api/retail/prices?$filter=armRegionName eq 'westus2' and armSkuName eq 'Standard_D4s_v5'
# Merge Items[]; dedupe by meterId.
```

### Production caller — Python with backoff + pagination

```python
import time
import requests

BASE = "https://prices.azure.com/api/retail/prices"
API_VERSION = "2023-01-01-preview"

def fetch_all_prices(filter_expr: str, currency: str = "USD", meter_region: str | None = "primary"):
    """Yield every Items[] row across all pages. Backs off on 429/5xx."""
    params = {"api-version": API_VERSION, "$filter": filter_expr, "currencyCode": currency}
    if meter_region:
        params["meterRegion"] = f"'{meter_region}'"
    url = BASE
    backoff = 1.0
    pages = 0
    while url:
        resp = requests.get(url, params=params if pages == 0 else None, timeout=30)
        if resp.status_code in (429, 500, 502, 503, 504):
            if backoff > 60:
                resp.raise_for_status()
            time.sleep(backoff)
            backoff *= 2
            continue
        resp.raise_for_status()
        backoff = 1.0
        body = resp.json()
        for item in body.get("Items", []):
            yield item
        url = body.get("NextPageLink")  # already includes encoded $skip
        params = None  # NextPageLink is self-contained; do not re-attach params
        pages += 1
```

### Production caller — TypeScript / Node

```ts
const BASE = "https://prices.azure.com/api/retail/prices";
const API_VERSION = "2023-01-01-preview";

export interface RetailPriceRow {
  currencyCode: string;
  retailPrice: number;
  unitPrice: number;
  armRegionName: string;
  location: string;
  effectiveStartDate: string;
  meterId: string;
  meterName: string;
  productId: string;
  skuId: string;
  productName: string;
  skuName: string;
  serviceName: string;
  serviceId: string;
  serviceFamily: string;
  unitOfMeasure: string;
  type: "Consumption" | "DevTestConsumption" | "Reservation";
  isPrimaryMeterRegion: boolean;
  armSkuName: string;
  reservationTerm?: "1 Year" | "3 Years";
  savingsPlan?: Array<{ unitPrice: number; retailPrice: number; term: "1 Year" | "3 Years" }>;
}

export async function* fetchAllPrices(filter: string, currency = "USD", meterRegion: string | null = "primary"): AsyncGenerator<RetailPriceRow> {
  const initial = new URL(BASE);
  initial.searchParams.set("api-version", API_VERSION);
  initial.searchParams.set("$filter", filter);
  initial.searchParams.set("currencyCode", currency);
  if (meterRegion) initial.searchParams.set("meterRegion", `'${meterRegion}'`);

  let url: string | null = initial.toString();
  let backoff = 1000;
  while (url) {
    const resp = await fetch(url);
    if ([429, 500, 502, 503, 504].includes(resp.status)) {
      if (backoff > 60_000) throw new Error(`prices.azure.com unavailable: ${resp.status}`);
      await new Promise(r => setTimeout(r, backoff));
      backoff *= 2;
      continue;
    }
    if (!resp.ok) throw new Error(`prices.azure.com ${resp.status}: ${await resp.text()}`);
    backoff = 1000;
    const body = await resp.json() as { Items: RetailPriceRow[]; NextPageLink: string | null };
    for (const row of body.Items) yield row;
    url = body.NextPageLink ?? null;
  }
}
```

### One-shot Bash (ad-hoc inspection only — never production)

```bash
curl -s 'https://prices.azure.com/api/retail/prices?api-version=2023-01-01-preview' \
  --data-urlencode "\$filter=armRegionName eq 'eastus' and armSkuName eq 'Standard_D4s_v5' and priceType eq 'Consumption'" \
  -G \
  | jq '.Items[] | {meterName, retailPrice, unitOfMeasure, type}'
```

`-G --data-urlencode` is the only correct way to embed `$filter` with single-quoted values; bash quoting eats them otherwise. Skips pagination and backoff — do not ship.

### Caching doctrine

Prices change at most monthly (typically aligned to calendar month). Production callers must cache:

| Layer | TTL | Key |
|---|---|---|
| HTTP cache (Varnish / CDN / in-process) | **24h minimum** | `(url, currency, api-version)` |
| Application snapshot table | **30 days** | `(serviceFamily, armRegionName, currencyCode, captured_at_utc)` |
| Drift-detection diff job | runs **daily** | compares today vs yesterday; alerts on row-level price change |

Emit two metrics: `azure_retail_prices_cache_hits_total`, `azure_retail_prices_origin_calls_total`. Sustained `origin/hits > 0.05` is a regression.

## Anti-patterns

| Anti-pattern | Symptom | Correct |
|---|---|---|
| Lowercase filter values on `2023-01-01-preview` | `Items: []`, no error | Match the case Microsoft uses verbatim |
| Omitting `?api-version=2023-01-01-preview` | `item.savingsPlan` always `undefined` | Pin the preview version on every call |
| Capping at first page | Silently undercounted SKUs; aggregate prices low by 10–100× | Walk `NextPageLink` to `null` |
| Reconstructing page URLs by hand | Brittle when MS adds cursor tokens | Follow `NextPageLink` verbatim |
| Reconciling non-USD prices to invoice | "your dashboard says €1,847 but Azure billed €1,902" | Mark non-USD as **reference rate**; reconcile only against USD |
| Mixing `Consumption` + `DevTestConsumption` rows | Apparent 50% discount that doesn't exist on EA | Filter `$filter=priceType eq 'Consumption'` |
| Treating `Reservation.retailPrice` as hourly | TCO off by 8,760× (1Y) or 26,280× (3Y) | Divide by term hours; document at call site |
| Trying `or` / `ne` / `startswith` in `$filter` | 400 Bad Request or empty `Items[]` | Two separate calls + client merge |
| Hard-coded `serviceName` without monitoring | Filter quietly returns 0 rows after MS rename | Monthly enum diff alert; prefer `serviceFamily` |
| Mixing `isPrimaryMeterRegion: true` and `false` | Double-counted prices | Filter `isPrimaryMeterRegion === true` (or `&meterRegion='primary'` on preview) |
| No backoff on 429 / 5xx | Cascading failure spreads from `prices.azure.com` | Exp backoff + jitter, max 5 retries, cap 60s |
| Calling on every dashboard render | Burns API capacity; risk of throttling | Cache ≥24h keyed on `(filter, currency, api-version)` |
| Using this API for invoice reconciliation | Numbers don't tie to invoice; finance loses trust | Cost Management Usage Details / Exports |
| Querying for Government / China prices | Returns commercial prices, mislabels them | Commercial Cloud only — use sovereign endpoints |

## Verification checklist

Before merging a caller, dashboard, or pipeline that reads from `prices.azure.com`:

- [ ] URL contains `?api-version=2023-01-01-preview` (or version is intentional + documented)
- [ ] All `$filter` string literals use exact MS casing (`'Virtual Machines'`, `'Compute'`, `'Reservation'`, etc.)
- [ ] Pagination loop terminates on `NextPageLink === null` and follows the link verbatim
- [ ] Backoff implemented: exp, jittered, cap 60s, max 5 retries on 429/5xx, no retry on 4xx
- [ ] Response cached with TTL ≥ 24h, keyed on `(filter, currency, api-version)`
- [ ] Cache hit / origin call metrics emitted
- [ ] Non-USD outputs labeled "reference rate; Microsoft bills in USD"
- [ ] `Reservation` rows divide `retailPrice` by term hours when displayed alongside Consumption hourly
- [ ] `DevTestConsumption` excluded from production cost models (or explicitly opt-in)
- [ ] Aggregations filter to `isPrimaryMeterRegion === true` (or use `&meterRegion='primary'`)
- [ ] Logical OR semantics implemented as separate calls + client merge
- [ ] `serviceName` filters have a monthly drift audit
- [ ] No assumption that this API matches an Azure invoice — reconciliation goes through Cost Management
- [ ] Commercial Cloud only — Government / China / sovereign callers redirected to sovereign endpoints

## Project-specific anchors

- This repo's load-test reports (`reports/loadtest-run-*.md`) cite tier names like `silver-pg`, `gold-pg`, `platinum-pg` and their Mongo counterparts. When a follow-up FinOps task asks "what does Platinum tier cost in `<region>`," this skill is the contract for the API call.
- `armSkuName` values relevant to current load tests: `Standard_D` series (PG flex), `Standard_M` / `Standard_E` (Cosmos vCore), `Standard_DS` (AKS nodepools).
- `serviceFamily` filters relevant: `Databases` (PG flex, Cosmos Mongo vCore), `Compute` (AKS nodepools, VMSS), `Containers` (AKS), `Storage` (Premium SSD v2 disks).
- Sister skill in this repo: `azure-pg-flex-empty-metrics-expertise` (observability), `azure-monitor-headroom-vs-raw-expertise` (metrics interpretation), `mongo-vcore-srv-not-a-record-expertise` (DocDB DNS trap).

## Reference

- MS docs: <https://learn.microsoft.com/en-us/rest/api/cost-management/retail-prices/azure-retail-prices>
- Live endpoint: <https://prices.azure.com/api/retail/prices>
- Supported currencies: <https://learn.microsoft.com/en-us/azure/cost-management-billing/microsoft-customer-agreement/microsoft-customer-agreement-faq#how-is-azure-priced-under-the-microsoft-customer-agreement>
- Cost Management Usage Details (for invoice reconciliation, **not** this API): <https://learn.microsoft.com/en-us/rest/api/consumption/usage-details>
