// =====================================================================
// Phase 4 of .omc/plans/kafka-loadtest-plan.md §6 (revision 4).
//
// Single parameterised k6 script driving the application's inbound Kafka
// adapter (`AccountEventConsumer` → Mediator → handlers → repository).
// Same script runs all six scenarios — Silver/Gold/Platinum × PG/Mongo.
//
// ENVIRONMENT VARIABLES
//   TIER   silver | gold | platinum                  (default: silver)
//   REPO   postgres | mongo                          (default: postgres)
//   RUNID  short timestamp identifier per run        (default: Date.now())
//
// Topic name derives from TIER + REPO and MUST already exist in the
// `messaging-system` namespace via tests/loadtest/kafka/kafkatopics-loadtest.yaml
// (Phase 3, MERGED in PR #57). The script verifies existence in setup()
// and aborts early with a descriptive error if the topic is missing — it
// does NOT auto-create, because partition count + RF + retention are
// declared by the KafkaTopic CRs, not by the load script.
//
// PER-RUNNER PEAK RATES (see .omc/progress.txt US-001 for the math)
//   silver   ramps 50  →  500 events/s     (matches Silver-PG ceiling §7.1)
//   gold     ramps 250 → 2500 events/s     (matches Gold ceilings   §7.1)
//   platinum ramps 530 → 5300 events/s     (matches Platinum-PG ceiling §7.1)
//
// TestRun spec.parallelism MUST be 1 in tests/loadtest/k6/testrun-kafka-{pg,mongo}.yaml.
// With parallelism > 1, the per-runner peaks above multiply by N runners
// and overshoot the §7.1 tier ceilings — see US-001 rationale.
//
// THRESHOLDS
//   kafka_writer_request_latency_ms{quantile="p(99)"} < 200
//     Tightened from plan §6.1's 500ms to align with §1 end-to-end P95
//     stop condition (200ms). Producer-side ceiling SHOULD be tighter
//     than end-to-end — if the producer alone is at 200ms p99, the
//     end-to-end SLO is already gone.
//
// RUNTIME IMAGE (required — k6/x/kafka extension is NOT in stock grafana/k6)
//   mostafamoradian/xk6-kafka:latest
//
// LOCAL DRY-RUN (Docker available, points at fast-data-dev)
//   docker run --rm -i -e TIER=silver -e REPO=postgres \
//     -v "$PWD/tests/loadtest/k6:/scripts" \
//     mostafamoradian/xk6-kafka:latest run --paused /scripts/kafka-account-events-loadtest.js
//
// IN-CLUSTER RUN (Phase 5)
//   1. kubectl create namespace testing-system  # if missing
//   2. kubectl create configmap hex-scaffold-kafka-loadtest \
//        -n testing-system \
//        --from-file=kafka-account-events-loadtest.js=tests/loadtest/k6/kafka-account-events-loadtest.js \
//        --dry-run=client -o yaml | kubectl apply -f -
//   3. sed -e "s/__TIER__/silver/" -e "s/__RUNID__/$(date +%s)/" \
//        tests/loadtest/k6/testrun-kafka-pg.yaml | kubectl apply -f -
//   4. kubectl get testruns -n testing-system -w
//   5. kubectl logs -n testing-system -l k6_cr=hex-scaffold-kafka-loadtest-pg -f
// =====================================================================

import { Producer, AdminClient } from "k6/x/kafka";
import { sleep } from "k6";
import { randomString } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

const TIER  = __ENV.TIER  || "silver";
const REPO  = __ENV.REPO  || "postgres";
const RUNID = __ENV.RUNID || `${Date.now()}`;

const BROKERS = ["hex-scaffold-loadtest-kafka-bootstrap.messaging-system.svc:9092"];
const TOPIC   = `v2.core.accounts.loadtest.${TIER}.${REPO === "postgres" ? "pg" : "mongo"}`;
const GROUP   = `hex-scaffold-loadtest-${TIER}-${REPO}-${RUNID}`;

const PROFILES = {
  silver:   { startRate:  50, peak:  500, preAllocatedVUs:  20, maxVUs:  60 },
  gold:     { startRate: 250, peak: 2500, preAllocatedVUs: 100, maxVUs: 350 },
  platinum: { startRate: 530, peak: 5300, preAllocatedVUs: 200, maxVUs: 720 },
};
const P = PROFILES[TIER];

const producer = new Producer({
  brokers:     BROKERS,
  topic:       TOPIC,
  acks:        "all",
  compression: "lz4",
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
        { duration: "1m",  target: Math.round(P.peak * 0.2) }, // warmup (discarded in analysis)
        { duration: "3m",  target: P.peak                   }, // ramp
        { duration: "10m", target: P.peak                   }, // steady-state (headline window)
        { duration: "1m",  target: 0                        }, // cool-down (drains in-flight)
      ],
      gracefulStop: "30s",
    },
  },
  thresholds: {
    kafka_writer_error_count:                              ["count==0"],
    "kafka_writer_request_latency_ms{quantile=\"p(99)\"}": ["value<200"],
  },
  discardResponseBodies: true,
  tags: { tier: TIER, repo: REPO, runId: RUNID },
};

// Verifies the topic exists; throws if missing. Phase 3 KafkaTopic CRs
// own partition count + RF + retention — this script must NOT auto-create
// or it would race the Topic Operator and end up with mismatched topology.
export function setup() {
  const admin = new AdminClient({ brokers: BROKERS });
  const topics = admin.listTopics();
  // xk6-kafka v2 build variants return { topic } or { name } — accept either.
  if (!topics.some(t => (t.topic || t.name) === TOPIC)) {
    admin.close();
    throw new Error(
      `topic ${TOPIC} missing — apply tests/loadtest/kafka/kafkatopics-loadtest.yaml ` +
      `and wait for KafkaTopic Ready before running this script.`
    );
  }
  admin.close();
  // Let metadata propagate to all brokers BEFORE VUs start producing.
  sleep(2);
  return { startedAt: Date.now(), topic: TOPIC, group: GROUP };
}

// ID strategy — supplied to the consumer via the Phase 1 §3.0
// CreateWithExternalId factory. IDs are bounded namespace
// `acct_LOADTEST_<tier>_<repo>_<vu>_<iter>`, ≤42 chars, fits the
// 64-char varchar(64) PK column verified in AccountConfiguration.cs:32.
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

  // Updates / deletes target an earlier-iteration ID from this VU's
  // namespace. At very low iteration counts, may target a missing row —
  // handler treats that as Result.Success per Phase 1 §3.3 / §3.4
  // idempotency. The "no-op rate" is tracked per-run.
  const baseIter = eventType === "AccountCreatedEvent"
    ? __ITER
    : Math.max(0, __ITER - 1 - Math.floor(Math.random() * 5));
  const id = `acct_LOADTEST_${TIER}_${REPO}_${__VU}_${baseIter}`;

  const value = eventType === "AccountDeletedEvent"
    ? { id, deleted_at_utc: new Date().toISOString(), metadata: { source: "k6-kafka" } }
    : buildAccountPayload(id);

  producer.produce({
    messages: [{
      key:   eventType,
      value: JSON.stringify(value),
      headers: { tier: TIER, repo: REPO, runId: RUNID, event_type: eventType },
    }],
  });
}

export function teardown() {
  producer.close();
}
