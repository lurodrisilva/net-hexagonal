// =====================================================================
// Phase 5 — Kafka inbound loadtest, k6 + xk6-kafka.
//
// Image: mostafamoradian/xk6-kafka:0.28.0  (k6 v0.54, xk6-kafka v0.28 = v1 API)
//   `:latest` ships glibc-built k6 on Alpine/musl base — broken; pin instead.
//   v0.28.0 exposes `Writer`/`Reader`/`Connection` (v1 API). The
//   v2 `Producer`/`Consumer`/`AdminClient` constructors only land in
//   xk6-kafka v0.31+, but those tags 404 on Docker Hub at the time of writing.
//
// ENVIRONMENT VARIABLES
//   TIER   silver | gold | platinum                  (default: silver)
//   REPO   postgres | mongo                          (default: postgres)
//   RUNID  short timestamp identifier per run        (default: Date.now())
//
// Topic name derives from TIER + REPO and MUST already exist in the
// `messaging-system` namespace via tests/loadtest/kafka/kafkatopics-loadtest.yaml.
// Verified in setup() — script aborts if the topic is missing.
//
// PER-RUNNER PEAK RATES (parallelism=1; one runner pod per TestRun)
//   silver   ramps  80 →  400 events/s
//   gold     ramps 500 → 2500 events/s
//   platinum ramps 1000 → 5000 events/s
// Phase 5 v2 — peaks aligned exactly with the per-tier sustained-throughput
// expectations (Silver=400/s, Gold=2500/s, Platinum=5000/s). Platinum
// preAllocatedVUs/maxVUs raised (200→400 / 720→1500) because Phase 5 v1
// already saw VU exhaustion at 3,238/s offered; the new 5,000/s target
// would exhaust the old maxVUs=720 immediately.
//
// THRESHOLDS — bound to v1-emitted metrics (kafka_writer_*).
// =====================================================================

import { Writer, Connection } from "k6/x/kafka";
import { sleep } from "k6";
import { b64encode } from "k6/encoding";

const _ALPHANUM = "abcdefghijklmnopqrstuvwxyz0123456789";
function randomString(n) {
  let s = "";
  for (let i = 0; i < n; i++) s += _ALPHANUM.charAt(Math.floor(Math.random() * _ALPHANUM.length));
  return s;
}

const TIER  = __ENV.TIER  || "silver";
const REPO  = __ENV.REPO  || "postgres";
const RUNID = __ENV.RUNID || `${Date.now()}`;

const BROKERS = ["hex-scaffold-loadtest-kafka-bootstrap.messaging-system.svc:9092"];
const TOPIC   = `v2.core.accounts.loadtest.${TIER}.${REPO === "postgres" ? "pg" : "mongo"}`;
const GROUP   = `hex-scaffold-loadtest-${TIER}-${REPO}-${RUNID}`;

const PROFILES = {
  silver:   { startRate:   80, peak:  400, preAllocatedVUs:  20, maxVUs:   60 },
  gold:     { startRate:  500, peak: 2500, preAllocatedVUs: 100, maxVUs:  350 },
  platinum: { startRate: 1000, peak: 5000, preAllocatedVUs: 400, maxVUs: 1500 },
};
const P = PROFILES[TIER];

// xk6-kafka v1 Writer — `compression` is the codec name; `autoCreateTopic`
// false because Phase 3 KafkaTopic CRs own partitions/RF/retention.
const writer = new Writer({
  brokers:         BROKERS,
  topic:           TOPIC,
  compression:     "lz4",
  autoCreateTopic: false,
});

export const options = {
  scenarios: {
    main: {
      executor:        "ramping-arrival-rate",
      startRate:       P.startRate,
      timeUnit:        "1s",
      preAllocatedVUs: P.preAllocatedVUs,
      maxVUs:          P.maxVUs,
      stages: [
        { duration: "1m",  target: Math.round(P.peak * 0.2) },
        { duration: "3m",  target: P.peak                   },
        { duration: "10m", target: P.peak                   },
        { duration: "1m",  target: 0                        },
      ],
      gracefulStop: "30s",
    },
  },
  // Thresholds removed for first run — xk6-kafka v0.28 metric names differ
  // from later versions; binding wrong names fails archive. Wire thresholds
  // in a follow-up after observing actual metric names from k6 stdout.
  discardResponseBodies: true,
  tags: { tier: TIER, repo: REPO, runId: RUNID },
};

// xk6-kafka v1 Connection.listTopics() returns a string[] of topic names.
export function setup() {
  const conn = new Connection({ address: BROKERS[0] });
  const topics = conn.listTopics();
  if (!topics.includes(TOPIC)) {
    conn.close();
    throw new Error(
      `topic ${TOPIC} missing — apply tests/loadtest/kafka/kafkatopics-loadtest.yaml ` +
      `and wait for KafkaTopic Ready before running this script.`
    );
  }
  conn.close();
  sleep(2);
  return { startedAt: Date.now(), topic: TOPIC, group: GROUP };
}

function buildAccountPayload(id) {
  return {
    id,
    applied_configurations: ["customer", "merchant"],
    contact_email:          `k6-${__VU}-${randomString(8)}@example.com`,
    display_name:           `k6-account-${__VU}-${randomString(6)}`,
    identity: {
      country:     "US",
      entity_type: "company",
      business_details: {
        registered_name: `k6-${__VU}-${randomString(8)}`,
        address:         { country: "US", postal_code: "10001" },
      },
    },
    configuration: { customer: { applied: true }, merchant: { applied: true } },
    metadata: {
      source: "k6-kafka",
      vu:     String(__VU),
      iter:   String(__ITER),
      tier:   TIER,
      repo:   REPO,
    },
  };
}

const MIX = { insert: 0.70, update: 0.25, delete: 0.05 };

export default function () {
  const r = Math.random();
  const eventType =
    r < MIX.insert                ? "AccountCreatedEvent" :
    r < MIX.insert + MIX.update   ? "AccountUpdatedEvent" :
                                    "AccountDeletedEvent";

  const baseIter = eventType === "AccountCreatedEvent"
    ? __ITER
    : Math.max(0, __ITER - 1 - Math.floor(Math.random() * 5));
  const id = `acct_LOADTEST_${TIER}_${REPO}_${__VU}_${baseIter}`;

  const value = eventType === "AccountDeletedEvent"
    ? { id, deleted_at_utc: new Date().toISOString(), metadata: { source: "k6-kafka" } }
    : buildAccountPayload(id);

  // xk6-kafka v0.28 v1 Writer expects base64-encoded byte payloads
  // for both key and value. (Newer v2 API took raw strings; this old build
  // requires the explicit b64encode hop.)
  writer.produce({
    messages: [{
      key:   b64encode(eventType),
      value: b64encode(JSON.stringify(value)),
      headers: { tier: TIER, repo: REPO, runId: RUNID, event_type: eventType },
    }],
  });
}

export function teardown() {
  writer.close();
}
