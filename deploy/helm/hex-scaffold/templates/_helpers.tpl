{{/* Standard helpers */}}
{{- define "hex-scaffold.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end }}

{{- define "hex-scaffold.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- if contains $name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}
{{- end }}

{{- define "hex-scaffold.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{ include "hex-scaffold.selectorLabels" . }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "hex-scaffold.selectorLabels" -}}
app.kubernetes.io/name: {{ include "hex-scaffold.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Image reference. A `digest` wins over `tag`, because CI pins the digest it just
built into the packaged umbrella (ADR-0014): chart version X then always deploys
exactly one image, with no mutable tag in between. Hand-written values keep using
`tag`.
*/}}
{{- define "hex-scaffold.image" -}}
{{- with .Values.image.digest -}}
{{ printf "%s@%s" $.Values.image.repository . }}
{{- else -}}
{{ printf "%s:%s" .Values.image.repository (.Values.image.tag | default .Chart.AppVersion) }}
{{- end }}
{{- end }}

{{- define "hex-scaffold.migrationImage" -}}
{{- with .Values.migrations.image.digest -}}
{{ printf "%s@%s" $.Values.migrations.image.repository . }}
{{- else -}}
{{ printf "%s:%s" .Values.migrations.image.repository (.Values.migrations.image.tag | default .Chart.AppVersion) }}
{{- end }}
{{- end }}

{{- define "hex-scaffold.wiremockFullname" -}}
{{- printf "%s-wiremock" (include "hex-scaffold.fullname" .) | trunc 63 | trimSuffix "-" -}}
{{- end }}

{{/*
Resolves the base URL the application's HttpClient ("ExternalApi") points at.
When wiremock.enabled, traffic is routed to the in-cluster WireMock service so
the outbound REST adapter (IExternalApiClient) hits the mock instead of an
external endpoint. Otherwise the value falls back to secrets.externalApiBaseUrl.
*/}}
{{- define "hex-scaffold.externalApiBaseUrl" -}}
{{- if .Values.wiremock.enabled -}}
{{- printf "http://%s:%v" (include "hex-scaffold.wiremockFullname" .) .Values.wiremock.service.port -}}
{{- else -}}
{{- .Values.secrets.externalApiBaseUrl -}}
{{- end -}}
{{- end }}

{{/* Guardrails — fail fast on nonsensical feature combos. */}}
{{- define "hex-scaffold.validateFeatures" -}}
{{- $f := .Values.features -}}
{{- if not (has $f.inbound (list "rest" "kafka")) -}}
{{- fail (printf "features.inbound must be 'rest' or 'kafka', got %q" $f.inbound) -}}
{{- end -}}
{{- if not (has $f.outbound (list "rest" "kafka")) -}}
{{- fail (printf "features.outbound must be 'rest' or 'kafka', got %q" $f.outbound) -}}
{{- end -}}
{{- if not (has $f.persistence (list "postgres" "mongo")) -}}
{{- fail (printf "features.persistence must be 'postgres' or 'mongo', got %q" $f.persistence) -}}
{{- end -}}
{{- if and $f.redis (ne $f.persistence "postgres") -}}
{{- fail "features.redis=true requires features.persistence='postgres' (Redis cache is only paired with PostgreSQL)." -}}
{{- end -}}
{{- end -}}

{{/*
Fails the render when bindBuildingBlock is enabled but under-specified. Every
field below must resolve to either a literal or a secretKeyRef; a missing one
used to render `Host=;` — a connection string that is syntactically fine and
silently wrong. Fail closed instead.
*/}}
{{- define "hex-scaffold.validatePgBind" -}}
{{- $b := .Values.postgres.bindBuildingBlock -}}
{{- if $b.enabled -}}
{{- if and (not $b.host) (not $b.hostFrom) -}}
{{- fail "\n\npostgres.bindBuildingBlock is enabled but neither `host` nor `hostFrom` is set.\n\nSet `host` for an in-cluster engine (cloudnativepg: <name>-rw), or `hostFrom`\nfor a Crossplane-provisioned server whose FQDN is unknown until it exists:\n\n  hostFrom: {name: <name>-postgres-conn, key: host}\n" -}}
{{- end -}}
{{- if and (not $b.database) (not $b.databaseFrom) -}}
{{- fail "\n\npostgres.bindBuildingBlock is enabled but neither `database` nor `databaseFrom` is set.\n" -}}
{{- end -}}
{{- if and (not $b.secretName) (not (and $b.usernameFrom $b.passwordFrom)) -}}
{{- fail "\n\npostgres.bindBuildingBlock is enabled but the credentials are unresolvable.\n\nSet `secretName` (one basic-auth Secret with username+password — cloudnativepg),\nor BOTH `usernameFrom` and `passwordFrom` — azure-flexibleserver splits them\nacross two Secrets:\n\n  usernameFrom: {name: <name>-postgres-conn,  key: username}\n  passwordFrom: {name: <name>-admin-password, key: password}\n" -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{/*
Emits one env var sourced from a Secret key, preferring an explicit {name,key}
override and falling back to a shared secret + conventional key.
Args: dict "var" <ENV NAME> "ref" <override or empty> "secret" <fallback> "key" <fallback key>

Ends with a trailing newline (`{{ end -}}`, not `{{- end -}}`) so callers can
chain it with `{{- include -}}` and still get one env entry per line.
*/}}
{{- define "hex-scaffold.pgBindSecretEnv" -}}
{{- $ref := .ref | default dict -}}
- name: {{ .var }}
  valueFrom:
    secretKeyRef:
      name: {{ $ref.name | default .secret | quote }}
      key: {{ $ref.key | default .key | quote }}
{{ end -}}

{{/*
Host + port for the building-block bind. Split out from pgBindEnv because the
migration Job's wait initContainer (G-T7) needs exactly these two and must not
be handed the credentials.

PGHOST is emitted as a secretKeyRef only when hostFrom is set — under
azure-flexibleserver the private FQDN does not exist until Crossplane creates
the server, so it cannot be templated. Under cloudnativepg the host is a
deterministic in-cluster Service name and stays a literal.
*/}}
{{- define "hex-scaffold.pgBindHostEnv" -}}
{{- $b := .Values.postgres.bindBuildingBlock -}}
{{- if $b.hostFrom -}}
{{- include "hex-scaffold.pgBindSecretEnv" (dict "var" "PGHOST" "ref" $b.hostFrom) -}}
{{- else -}}
- name: PGHOST
  value: {{ $b.host | quote }}
{{ end -}}
- name: PGPORT
  value: {{ $b.port | quote }}
{{ end -}}

{{/*
Postgres binding to a platform building block.
When postgres.bindBuildingBlock.enabled, the app is wired to the SQL building
block instead of a literal connection string. Credentials flow ONLY via
secretKeyRef; the connection string composes them with Kubernetes $(VAR)
expansion — every PG* var is declared BEFORE the connection string so the
kubelet can expand $(...) at container start. No plaintext credential is ever
templated into a manifest.

Each field takes a per-field secretKeyRef override (G-T6) or a literal. That is
the whole engine story from this chart's side: it never learns an engine name.
*/}}
{{- define "hex-scaffold.pgBindEnv" -}}
{{- $b := .Values.postgres.bindBuildingBlock -}}
{{- include "hex-scaffold.validatePgBind" . -}}
{{- include "hex-scaffold.pgBindHostEnv" . -}}
{{- if $b.databaseFrom -}}
{{- include "hex-scaffold.pgBindSecretEnv" (dict "var" "PGDATABASE" "ref" $b.databaseFrom) -}}
{{- end -}}
{{- include "hex-scaffold.pgBindSecretEnv" (dict "var" "PGUSER" "ref" $b.usernameFrom "secret" $b.secretName "key" "username") -}}
{{- include "hex-scaffold.pgBindSecretEnv" (dict "var" "PGPASSWORD" "ref" $b.passwordFrom "secret" $b.secretName "key" "password") -}}
- name: ConnectionStrings__PostgreSql
  value: "Host=$(PGHOST);Port=$(PGPORT);Database={{ if $b.databaseFrom }}$(PGDATABASE){{ else }}{{ $b.database }}{{ end }};Username=$(PGUSER);Password=$(PGPASSWORD);Maximum Pool Size=100"
{{- end -}}
