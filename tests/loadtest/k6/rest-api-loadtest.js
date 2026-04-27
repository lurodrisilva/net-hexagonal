/*
 * hex-scaffold — REST API load test
 *
 * Exercises the full Sample CRUD surface (POST/GET/LIST/PUT/DELETE) with a
 * ramped VU profile that models: warmup -> steady state -> peak -> drain.
 *
 * Custom metrics map onto the four golden signals (latency/traffic/errors/
 * saturation) so the output lines up 1:1 with what Application Insights /
 * Azure Monitor displays in the "Application Map" + "Live Metrics" views.
 *
 * Execution modes
 *   - Local:     k6 run tests/loadtest/k6/rest-api-loadtest.js
 *   - Operator:  kubectl apply -f tests/loadtest/k6/testrun.yaml
 *
 * Configurable via environment variables:
 *   BASE_URL   (default http://hex-scaffold.default.svc:80)
 *   VUS        (override default ramping profile with a flat VU count)
 *   DURATION   (used together with VUS for a flat profile)
 *
 * Rate limiter coupling
 *   The API ships with a per-IP fixed-window limiter (RateLimitOptions).
 *   The default chart cap is 100 req/min/IP; the ramped profile below
 *   peaks at 150 req/s, so unless `rateLimit.permitLimit` is bumped well
 *   above the test's arrival rate the runners will be 429-throttled. 429s
 *   are tracked in `golden_throttled_rate` separately from real errors so
 *   a working limiter doesn't masquerade as a system fault — see
 *   classifyResponse below.
 */

import http from "k6/http";
import { check, group, sleep } from "k6";
import { Counter, Rate, Trend } from "k6/metrics";
import { randomIntBetween, randomString } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

const BASE_URL = __ENV.BASE_URL || "http://hex-scaffold.default.svc:80";

// --- Four golden signals as custom metrics ---------------------------------
const latencyCreate = new Trend("golden_latency_create_ms", true);
const latencyRead   = new Trend("golden_latency_read_ms",   true);
const latencyList   = new Trend("golden_latency_list_ms",   true);
const latencyUpdate = new Trend("golden_latency_update_ms", true);
const latencyDelete = new Trend("golden_latency_delete_ms", true);
const trafficRps    = new Counter("golden_traffic_total_requests");
const errorsRate    = new Rate("golden_errors_rate");
// Track 429s separately. A working rate limiter under load *should* reject
// excess traffic — counting that as an error makes the test report the
// limiter as a system fault rather than as expected backpressure. Real
// errors are 5xx and unexpected 4xx (NOT 429); see classifyResponse.
const throttledRate = new Rate("golden_throttled_rate");
const saturation    = new Trend("golden_saturation_concurrent_vus");

// safeJson — the response body is text on non-2xx and on transport errors.
// k6's res.json() throws in those cases, taking the whole iteration with it
// when the same status check would have flagged the failure cleanly.
function safeJson(res) {
  try { return res.json(); } catch (_) { return null; }
}

// classifyResponse — drive errorsRate and throttledRate from one source of
// truth so the per-call site doesn't have to remember whether 429 counts as
// an error this week. Returns { ok, throttled } so the caller can decide
// whether to continue the iteration (e.g. skip GET/PUT/DELETE on 429-Create).
function classifyResponse(res, expectedStatus) {
  const status = res.status | 0;
  const throttled = status === 429;
  const ok = Array.isArray(expectedStatus)
    ? expectedStatus.includes(status)
    : status === expectedStatus;
  // Anything that's neither the happy path nor a throttle is a real error.
  throttledRate.add(throttled);
  errorsRate.add(!ok && !throttled);
  return { ok, throttled };
}

// ---------------------------------------------------------------------------
export const options = (() => {
  if (__ENV.VUS && __ENV.DURATION) {
    // Flat-profile override (useful for quick smoke runs).
    return {
      vus: parseInt(__ENV.VUS, 10),
      duration: __ENV.DURATION,
      thresholds: thresholds(),
    };
  }
  return {
    scenarios: {
      // Scenario 1 — CRUD happy path under ramped constant-arrival-rate load.
      crud_ramp: {
        executor: "ramping-arrival-rate",
        exec: "crudFlow",
        startRate: 5,
        timeUnit: "1s",
        preAllocatedVUs: 20,
        maxVUs: 200,
        stages: [
          { target: 10,  duration: "30s" },   // warmup
          { target: 50,  duration: "1m"  },   // steady
          { target: 150, duration: "1m"  },   // peak
          { target: 20,  duration: "30s" },   // drain
          { target: 0,   duration: "15s" },
        ],
      },
      // Scenario 2 — hot reads against /samples (list) to keep cache warm.
      read_heat: {
        executor: "constant-arrival-rate",
        exec: "listOnly",
        startTime: "30s",
        duration: "2m",
        rate: 30,
        timeUnit: "1s",
        preAllocatedVUs: 10,
        maxVUs: 40,
      },
    },
    thresholds: thresholds(),
  };
})();

function thresholds() {
  return {
    // Latency (p95 / p99) — golden signal 1
    "http_req_duration{name:create}":       ["p(95)<400", "p(99)<800"],
    "http_req_duration{name:get}":          ["p(95)<200", "p(99)<400"],
    "http_req_duration{name:list}":         ["p(95)<300", "p(99)<600"],
    "http_req_duration{name:update}":       ["p(95)<400", "p(99)<800"],
    "http_req_duration{name:delete}":       ["p(95)<400", "p(99)<800"],
    // Errors — golden signal 3. golden_errors_rate excludes 429 by design
    // (see classifyResponse) so this gate fires on real failures only;
    // http_req_failed is a coarse k6-native sanity check that does count
    // 429s, so we leave it slightly looser to allow incidental throttling.
    "http_req_failed":                      ["rate<0.05"],  // <5% non-2xx (incl. throttling)
    "golden_errors_rate":                   ["rate<0.01"],  // <1% non-2xx, non-429
    // Throttling — informational gate. >50% throttled means the run never
    // got past the limiter and the latency/error numbers can't be trusted.
    // Bump rateLimit.permitLimit in the chart and rerun.
    "golden_throttled_rate":                ["rate<0.50"],
    // Traffic — golden signal 2 (informational; assert minimum volume reached)
    "golden_traffic_total_requests":        ["count>500"],
    // Saturation — golden signal 4 (VU utilization ceiling)
    "golden_saturation_concurrent_vus":     ["p(95)<180"],
  };
}

// ---------------------------------------------------------------------------
// CRUD flow: POST -> GET -> PUT -> DELETE, verifying each step.
export function crudFlow() {
  saturation.add(__VU);

  const payload = JSON.stringify({
    // The server generates the Id (int), so we only send Name + Description.
    // Schema: Name varchar(200) NOT NULL; Description varchar(1000) NULL.
    Name:        `k6-sample-${__VU}-${randomString(8)}`,
    Description: `Created by VU ${__VU} iter ${__ITER} at ${new Date().toISOString()}`,
  });

  const headers = { "Content-Type": "application/json" };
  let createdId = null;

  group("create", () => {
    const t0 = Date.now();
    const res = http.post(`${BASE_URL}/samples`, payload, {
      headers,
      tags: { name: "create" },
    });
    latencyCreate.add(Date.now() - t0);
    trafficRps.add(1);
    const verdict = classifyResponse(res, 201);
    check(res, {
      "POST /samples status 201 or 429": (r) => r.status === 201 || r.status === 429,
      "POST /samples returns Id when 201": (r) => {
        if (r.status !== 201) return true; // not applicable
        const j = safeJson(r);
        return j !== null && (typeof j.id === "number" || typeof j.Id === "number");
      },
    });
    if (verdict.ok) {
      const body = safeJson(res);
      createdId = body?.id ?? body?.Id ?? null;
    }
  });
  // Either Create errored or was throttled; nothing to GET/PUT/DELETE.
  if (!createdId) return;

  sleep(randomIntBetween(0, 1));

  group("get", () => {
    const t0 = Date.now();
    const res = http.get(`${BASE_URL}/samples/${createdId}`, { tags: { name: "get" } });
    latencyRead.add(Date.now() - t0);
    trafficRps.add(1);
    classifyResponse(res, 200);
    if (res.status === 200) {
      // SampleRecord shape: {id, name, status, description}. Catches an
      // accidental contract regression (e.g., a Sample-aggregate refactor
      // breaking EF rehydration or the inbound DTO mapper).
      check(res, {
        "GET /samples/:id matches id": (r) => safeJson(r)?.id === createdId,
        "GET /samples/:id has name":   (r) => typeof safeJson(r)?.name === "string",
        "GET /samples/:id has status": (r) => typeof safeJson(r)?.status === "string",
      });
    }
  });

  group("update", () => {
    // UpdateSampleRequest only accepts {Name, Description}. There is no
    // Status field on the inbound DTO — Activate/Deactivate are domain
    // operations not exposed as endpoints — so sending Status here would
    // be a no-op silently dropped by System.Text.Json.
    const body = JSON.stringify({
      Name:        `k6-updated-${__VU}-${randomString(6)}`,
      Description: "Updated by k6 load test",
    });
    const t0 = Date.now();
    const res = http.put(`${BASE_URL}/samples/${createdId}`, body, {
      headers,
      tags: { name: "update" },
    });
    latencyUpdate.add(Date.now() - t0);
    trafficRps.add(1);
    classifyResponse(res, 200);
    if (res.status === 200) {
      check(res, {
        "PUT /samples/:id matches id":   (r) => safeJson(r)?.id === createdId,
        "PUT /samples/:id name updated": (r) => (safeJson(r)?.name ?? "").startsWith("k6-updated-"),
      });
    }
  });

  group("delete", () => {
    const t0 = Date.now();
    const res = http.del(`${BASE_URL}/samples/${createdId}`, null, {
      tags: { name: "delete" },
    });
    latencyDelete.add(Date.now() - t0);
    trafficRps.add(1);
    classifyResponse(res, 204);
  });
}

// Read-heavy scenario: keeps LIST hot to exercise the cache & query path.
export function listOnly() {
  saturation.add(__VU);
  const res = http.get(`${BASE_URL}/samples`, { tags: { name: "list" } });
  latencyList.add(res.timings.duration);
  trafficRps.add(1);
  classifyResponse(res, 200);
  if (res.status === 200) {
    // PagedResult shape: {items[], page, perPage, totalCount, totalPages}.
    check(res, {
      "GET /samples is paged result": (r) => Array.isArray(safeJson(r)?.items),
      "GET /samples has page meta":   (r) => typeof safeJson(r)?.page === "number"
                                          && typeof safeJson(r)?.totalPages === "number",
    });
  }
}

// handleSummary is emitted at end-of-run. In the k6-operator flow this is
// auto-archived as a TestRun status artifact.
export function handleSummary(data) {
  return {
    stdout: textSummary(data),
    "summary.json": JSON.stringify(data, null, 2),
  };
}

function textSummary(data) {
  const m = data.metrics;
  // Guard against missing metric / missing percentile. Calling .toFixed() on
  // the "-" fallback string raised a TypeError in the original implementation
  // and crashed handleSummary right after the threshold report.
  const fmt = (n, p) => {
    const v = n && n.values ? n.values[p] : undefined;
    return typeof v === "number" ? v.toFixed(2) : "n/a";
  };
  const ratePct = (n) => (n && n.values && typeof n.values.rate === "number"
    ? (n.values.rate * 100).toFixed(2) + "%"
    : "n/a");
  return `
======== hex-scaffold REST load-test summary ========
Requests:                ${m.http_reqs?.values?.count ?? "n/a"}
Error rate (5xx/4xx):    ${ratePct(m.golden_errors_rate)}
Throttled rate (429):    ${ratePct(m.golden_throttled_rate)}
http_req_failed (k6):    ${ratePct(m.http_req_failed)}

Latency (ms)             p50        p95        p99
  create                 ${fmt(m["http_req_duration{name:create}"], "p(50)")}     ${fmt(m["http_req_duration{name:create}"], "p(95)")}     ${fmt(m["http_req_duration{name:create}"], "p(99)")}
  get                    ${fmt(m["http_req_duration{name:get}"],    "p(50)")}     ${fmt(m["http_req_duration{name:get}"],    "p(95)")}     ${fmt(m["http_req_duration{name:get}"],    "p(99)")}
  list                   ${fmt(m["http_req_duration{name:list}"],   "p(50)")}     ${fmt(m["http_req_duration{name:list}"],   "p(95)")}     ${fmt(m["http_req_duration{name:list}"],   "p(99)")}
  update                 ${fmt(m["http_req_duration{name:update}"], "p(50)")}     ${fmt(m["http_req_duration{name:update}"], "p(95)")}     ${fmt(m["http_req_duration{name:update}"], "p(99)")}
  delete                 ${fmt(m["http_req_duration{name:delete}"], "p(50)")}     ${fmt(m["http_req_duration{name:delete}"], "p(95)")}     ${fmt(m["http_req_duration{name:delete}"], "p(99)")}

Saturation (concurrent VUs, p95): ${fmt(m.golden_saturation_concurrent_vus, "p(95)")}
======================================================
`;
}
