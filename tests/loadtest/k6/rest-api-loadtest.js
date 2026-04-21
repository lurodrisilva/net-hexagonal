/*
 * hex-scaffold — REST API load test
 *
 * Exercises the full Sample CRUD surface (POST/GET/LIST/PATCH/DELETE) with a
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
const saturation    = new Trend("golden_saturation_concurrent_vus");

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
    // Errors — golden signal 3
    "http_req_failed":                      ["rate<0.01"],  // <1% non-2xx
    "golden_errors_rate":                   ["rate<0.01"],
    // Traffic — golden signal 2 (informational; assert minimum volume reached)
    "golden_traffic_total_requests":        ["count>500"],
    // Saturation — golden signal 4 (VU utilization ceiling)
    "golden_saturation_concurrent_vus":     ["p(95)<180"],
  };
}

// ---------------------------------------------------------------------------
// CRUD flow: POST -> GET -> PATCH -> DELETE, verifying each step.
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
    const ok = check(res, {
      "POST /samples status 201": (r) => r.status === 201,
      "POST /samples returns Id": (r) => {
        try { return typeof r.json().id === "number" || typeof r.json().Id === "number"; }
        catch (_) { return false; }
      },
    });
    errorsRate.add(!ok);
    if (ok) {
      const body = res.json();
      createdId = body.id ?? body.Id;
    }
  });
  if (!createdId) return;

  sleep(randomIntBetween(0, 1));

  group("get", () => {
    const t0 = Date.now();
    const res = http.get(`${BASE_URL}/samples/${createdId}`, { tags: { name: "get" } });
    latencyRead.add(Date.now() - t0);
    trafficRps.add(1);
    const ok = check(res, {
      "GET /samples/:id status 200": (r) => r.status === 200,
    });
    errorsRate.add(!ok);
  });

  group("update", () => {
    const body = JSON.stringify({
      Name:        `k6-updated-${__VU}-${randomString(6)}`,
      Description: "Updated by k6 load test",
      Status:      "Active",
    });
    const t0 = Date.now();
    const res = http.put(`${BASE_URL}/samples/${createdId}`, body, {
      headers,
      tags: { name: "update" },
    });
    latencyUpdate.add(Date.now() - t0);
    trafficRps.add(1);
    const ok = check(res, {
      "PUT /samples/:id status 2xx": (r) => r.status >= 200 && r.status < 300,
    });
    errorsRate.add(!ok);
  });

  group("delete", () => {
    const t0 = Date.now();
    const res = http.del(`${BASE_URL}/samples/${createdId}`, null, {
      tags: { name: "delete" },
    });
    latencyDelete.add(Date.now() - t0);
    trafficRps.add(1);
    const ok = check(res, {
      "DELETE /samples/:id status 2xx": (r) => r.status >= 200 && r.status < 300,
    });
    errorsRate.add(!ok);
  });
}

// Read-heavy scenario: keeps LIST hot to exercise the cache & query path.
export function listOnly() {
  saturation.add(__VU);
  const res = http.get(`${BASE_URL}/samples`, { tags: { name: "list" } });
  latencyList.add(res.timings.duration);
  trafficRps.add(1);
  const ok = check(res, {
    "GET /samples status 200": (r) => r.status === 200,
  });
  errorsRate.add(!ok);
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
  const fmt = (n, p) => (n && n.values ? (n.values[p] ?? "-").toFixed(2) : "n/a");
  return `
======== hex-scaffold REST load-test summary ========
Requests:                ${m.http_reqs?.values?.count ?? "n/a"}
Error rate:              ${(m.http_req_failed?.values?.rate ?? 0).toFixed(4)}

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
