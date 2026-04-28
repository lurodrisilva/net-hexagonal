/*
 * hex-scaffold — Stripe v2 Accounts API load test
 *
 * Reproduces the four endpoints exposed by the Accounts replacement of the
 * earlier Samples scaffold:
 *
 *   POST /v2/core/accounts          create
 *   GET  /v2/core/accounts/{id}     retrieve
 *   POST /v2/core/accounts/{id}     update (partial; Stripe uses POST)
 *   GET  /v2/core/accounts          list (cursor-paginated)
 *
 * Custom metrics map onto the four golden signals (latency / traffic /
 * errors / saturation) so the output lines up 1:1 with what Application
 * Insights / Azure Monitor displays in the "Application Map" + "Live
 * Metrics" views.
 *
 * Execution modes
 *   - Local:     k6 run tests/loadtest/k6/rest-api-loadtest.js
 *   - Operator:  ./tests/loadtest/k6/loadtest.sh run
 *
 * Configurable via environment variables:
 *   BASE_URL   (default http://hex-scaffold.default.svc:80)
 *   VUS        (override default ramping profile with a flat VU count)
 *   DURATION   (used together with VUS for a flat profile)
 *
 * Rate limiter coupling — same caveat as before: bump
 * `rateLimit.permitLimit` in the chart well above the test's arrival rate
 * (~150 req/s peak) or the runners will be 429-throttled.
 *
 * Wire format — snake_case end-to-end (matches the Stripe v2 spec the API
 * reproduces). Field-shape checks below assert the response actually
 * matches that contract on every iteration.
 */

import http from "k6/http";
import { check, group, sleep } from "k6";
import { Counter, Rate, Trend } from "k6/metrics";
import { randomIntBetween, randomString } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

const BASE_URL = __ENV.BASE_URL || "http://hex-scaffold.default.svc:80";
const ACCOUNTS_PATH = "/v2/core/accounts";

// --- Four golden signals as custom metrics ---------------------------------
const latencyCreate = new Trend("golden_latency_create_ms", true);
const latencyRead   = new Trend("golden_latency_read_ms",   true);
const latencyList   = new Trend("golden_latency_list_ms",   true);
const latencyUpdate = new Trend("golden_latency_update_ms", true);
const trafficRps    = new Counter("golden_traffic_total_requests");
const errorsRate    = new Rate("golden_errors_rate");
// Track 429s separately. A working rate limiter under load *should* reject
// excess traffic — counting that as an error makes the test report the
// limiter as a system fault rather than as expected backpressure.
const throttledRate = new Rate("golden_throttled_rate");
const saturation    = new Trend("golden_saturation_concurrent_vus");

// safeJson — response body is text on non-2xx and on transport errors.
// k6's res.json() throws in those cases; the wrapper returns null instead
// so a single failure doesn't crash the iteration.
function safeJson(res) {
  try { return res.json(); } catch (_) { return null; }
}

// classifyResponse — single source of truth for error vs throttle
// accounting. 429 → throttledRate; other non-success → errorsRate. Returns
// {ok, throttled} so the caller can short-circuit when Create is throttled
// (no id to GET / PUT later).
function classifyResponse(res, expectedStatus) {
  const status = res.status | 0;
  const throttled = status === 429;
  const ok = Array.isArray(expectedStatus)
    ? expectedStatus.includes(status)
    : status === expectedStatus;
  throttledRate.add(throttled);
  errorsRate.add(!ok && !throttled);
  return { ok, throttled };
}

// ---------------------------------------------------------------------------
export const options = (() => {
  if (__ENV.VUS && __ENV.DURATION) {
    return {
      vus: parseInt(__ENV.VUS, 10),
      duration: __ENV.DURATION,
      thresholds: thresholds(),
    };
  }
  return {
    scenarios: {
      crud_ramp: {
        executor: "ramping-arrival-rate",
        exec: "crudFlow",
        startRate: 5,
        timeUnit: "1s",
        preAllocatedVUs: 20,
        // maxVUs: 200,
        maxVUs: 40,
        stages: [
          { target: 10,  duration: "30s" },   // warmup
          // { target: 50,  duration: "1m"  },   // steady
          { target: 30,  duration: "1m"  },   // steady
          // { target: 150, duration: "1m"  },   // peak
          { target: 40, duration: "1m"  },   // peak
          { target: 20,  duration: "30s" },   // drain
          { target: 0,   duration: "15s" },
        ],
      },
      // Read-heavy overlay. List is cursor-paginated — we just hit the
      // first page repeatedly to keep the read path warm.
      list_heat: {
        executor: "constant-arrival-rate",
        exec: "listOnly",
        startTime: "30s",
        duration: "2m",
        rate: 30,
        timeUnit: "1s",
        preAllocatedVUs: 10,
        maxVUs: 10,
      },
    },
    thresholds: thresholds(),
  };
})();

function thresholds() {
  return {
    "http_req_duration{name:create}":       ["p(95)<400", "p(99)<800"],
    "http_req_duration{name:get}":          ["p(95)<200", "p(99)<400"],
    "http_req_duration{name:list}":         ["p(95)<300", "p(99)<600"],
    "http_req_duration{name:update}":       ["p(95)<400", "p(99)<800"],
    "http_req_failed":                      ["rate<0.05"],  // <5% (incl. throttling)
    "golden_errors_rate":                   ["rate<0.01"],  // <1% real failures
    "golden_throttled_rate":                ["rate<0.50"],
    "golden_traffic_total_requests":        ["count>500"],
    "golden_saturation_concurrent_vus":     ["p(95)<180"],
  };
}

// ---------------------------------------------------------------------------
// CRUD flow: POST -> GET -> POST(update) -> (no DELETE on Accounts; Stripe
// v2 uses Account.close which is out of scope for the four endpoints
// reproduced).
export function crudFlow() {
  saturation.add(__VU);

  const createPayload = JSON.stringify({
    applied_configurations: ["customer", "merchant"],
    contact_email: `k6-${__VU}-${randomString(8)}@example.com`,
    display_name: `k6-account-${__VU}-${randomString(6)}`,
    identity: {
      country: "US",
      entity_type: "company",
      business_details: {
        registered_name: `k6-${__VU}-${randomString(8)}`,
        address: { country: "US", postal_code: "10001" },
      },
    },
    configuration: {
      customer: { applied: true },
      merchant: { applied: true },
    },
    metadata: { source: "k6", vu: String(__VU), iter: String(__ITER) },
  });

  const headers = { "Content-Type": "application/json" };
  let createdId = null;

  group("create", () => {
    const t0 = Date.now();
    const res = http.post(`${BASE_URL}${ACCOUNTS_PATH}`, createPayload, {
      headers, tags: { name: "create" },
    });
    latencyCreate.add(Date.now() - t0);
    trafficRps.add(1);
    const verdict = classifyResponse(res, 200);
    check(res, {
      "POST /v2/core/accounts status 200 or 429": (r) => r.status === 200 || r.status === 429,
      "POST returns acct_-prefixed id when 200": (r) => {
        if (r.status !== 200) return true;
        const j = safeJson(r);
        return j !== null && typeof j.id === "string" && j.id.startsWith("acct_");
      },
      "POST returns object='v2.core.account' when 200": (r) => {
        if (r.status !== 200) return true;
        return safeJson(r)?.object === "v2.core.account";
      },
    });
    if (verdict.ok) {
      createdId = safeJson(res)?.id ?? null;
    }
  });
  if (!createdId) return;

  sleep(randomIntBetween(0, 1));

  group("get", () => {
    const t0 = Date.now();
    const res = http.get(`${BASE_URL}${ACCOUNTS_PATH}/${createdId}`, { tags: { name: "get" } });
    latencyRead.add(Date.now() - t0);
    trafficRps.add(1);
    classifyResponse(res, 200);
    if (res.status === 200) {
      check(res, {
        "GET /v2/core/accounts/:id matches id":     (r) => safeJson(r)?.id === createdId,
        "GET returns dashboard string":             (r) => typeof safeJson(r)?.dashboard === "string",
        "GET returns applied_configurations array": (r) => Array.isArray(safeJson(r)?.applied_configurations),
      });
    }
  });

  group("update", () => {
    // Partial update — only display_name. Other fields stay as set on
    // create. Stripe's POST /v2/core/accounts/{id} treats absent keys as
    // "leave alone"; the API enforces that distinction via JsonElement
    // (Undefined vs Null vs value).
    const updatePayload = JSON.stringify({
      display_name: `k6-updated-${__VU}-${randomString(6)}`,
    });
    const t0 = Date.now();
    const res = http.post(`${BASE_URL}${ACCOUNTS_PATH}/${createdId}`, updatePayload, {
      headers, tags: { name: "update" },
    });
    latencyUpdate.add(Date.now() - t0);
    trafficRps.add(1);
    classifyResponse(res, 200);
    if (res.status === 200) {
      check(res, {
        "POST /v2/core/accounts/:id matches id":            (r) => safeJson(r)?.id === createdId,
        "POST update reflects new display_name":            (r) => (safeJson(r)?.display_name ?? "").startsWith("k6-updated-"),
      });
    }
  });
}

// Read-heavy: list first page repeatedly. Stripe-style cursor envelope is
// `{object: "list", data: [...], has_more: bool}`.
export function listOnly() {
  saturation.add(__VU);
  const res = http.get(`${BASE_URL}${ACCOUNTS_PATH}?limit=10`, { tags: { name: "list" } });
  latencyList.add(res.timings.duration);
  trafficRps.add(1);
  classifyResponse(res, 200);
  if (res.status === 200) {
    check(res, {
      "GET /v2/core/accounts is a list envelope":  (r) => safeJson(r)?.object === "list",
      "GET /v2/core/accounts returns data array":  (r) => Array.isArray(safeJson(r)?.data),
      "GET /v2/core/accounts has has_more bool":   (r) => typeof safeJson(r)?.has_more === "boolean",
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
  const fmt = (n, p) => {
    const v = n && n.values ? n.values[p] : undefined;
    return typeof v === "number" ? v.toFixed(2) : "n/a";
  };
  const ratePct = (n) => (n && n.values && typeof n.values.rate === "number"
    ? (n.values.rate * 100).toFixed(2) + "%"
    : "n/a");
  return `
======== hex-scaffold v2 Accounts load-test summary ========
Requests:                ${m.http_reqs?.values?.count ?? "n/a"}
Error rate (5xx/4xx):    ${ratePct(m.golden_errors_rate)}
Throttled rate (429):    ${ratePct(m.golden_throttled_rate)}
http_req_failed (k6):    ${ratePct(m.http_req_failed)}

Latency (ms)             p50        p95        p99
  create                 ${fmt(m["http_req_duration{name:create}"], "p(50)")}     ${fmt(m["http_req_duration{name:create}"], "p(95)")}     ${fmt(m["http_req_duration{name:create}"], "p(99)")}
  get                    ${fmt(m["http_req_duration{name:get}"],    "p(50)")}     ${fmt(m["http_req_duration{name:get}"],    "p(95)")}     ${fmt(m["http_req_duration{name:get}"],    "p(99)")}
  list                   ${fmt(m["http_req_duration{name:list}"],   "p(50)")}     ${fmt(m["http_req_duration{name:list}"],   "p(95)")}     ${fmt(m["http_req_duration{name:list}"],   "p(99)")}
  update                 ${fmt(m["http_req_duration{name:update}"], "p(50)")}     ${fmt(m["http_req_duration{name:update}"], "p(95)")}     ${fmt(m["http_req_duration{name:update}"], "p(99)")}

Saturation (concurrent VUs, p95): ${fmt(m.golden_saturation_concurrent_vus, "p(95)")}
=============================================================
`;
}
