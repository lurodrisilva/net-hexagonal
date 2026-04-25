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

{{- define "hex-scaffold.image" -}}
{{- $tag := .Values.image.tag | default .Chart.AppVersion -}}
{{ printf "%s:%s" .Values.image.repository $tag }}
{{- end }}

{{- define "hex-scaffold.migrationImage" -}}
{{- $tag := .Values.migrations.image.tag | default .Chart.AppVersion -}}
{{ printf "%s:%s" .Values.migrations.image.repository $tag }}
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
