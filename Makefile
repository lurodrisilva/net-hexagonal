# ==================================================================================== #
# PROLOGUE — safe, portable, self-documenting
# ==================================================================================== #
# Requires GNU Make 4.0+.
# macOS ships /usr/bin/make 3.81 (GPLv2). Install a modern make:
#   brew install make   # then invoke as `gmake` or put $(brew --prefix make)/libexec/gnubin first on PATH
SHELL := /usr/bin/env bash
.SHELLFLAGS := -eu -o pipefail -c
.DELETE_ON_ERROR:
MAKEFLAGS += --warn-undefined-variables --no-builtin-rules
.DEFAULT_GOAL := help

# ==================================================================================== #
# VARIABLES
# ==================================================================================== #
DOTNET             ?= dotnet
DOCKER             ?= docker

SLN                ?= Hex.Scaffold.slnx
PROJECT            ?= src/Hex.Scaffold.Api/Hex.Scaffold.Api.csproj
MIGRATIONS_PROJECT ?= src/Hex.Scaffold.Adapters.Persistence/Hex.Scaffold.Adapters.Persistence.csproj
STARTUP_PROJECT    ?= src/Hex.Scaffold.Api/Hex.Scaffold.Api.csproj

TESTS_UNIT         ?= tests/Hex.Scaffold.Tests.Unit
TESTS_INTEGRATION  ?= tests/Hex.Scaffold.Tests.Integration
TESTS_ARCHITECTURE ?= tests/Hex.Scaffold.Tests.Architecture

CONFIG             ?= Release
PORT               ?= 8080

IMAGE              ?= hex-scaffold
TAG                ?= $(shell git rev-parse --short HEAD 2>/dev/null || echo latest)

COVERAGE_DIR       ?= coverage
PUBLISH_DIR        ?= publish
RID                ?= linux-x64

# ==================================================================================== #
# HELPERS
# ==================================================================================== #

.PHONY: help
help: ## Show this help
	@awk 'BEGIN {FS = ":.*?## "; printf "\nUsage: make \033[36m<target>\033[0m\n\nTargets:\n"} \
	     /^[a-zA-Z_0-9%\/-]+:.*?## / {printf "  \033[36m%-22s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)
	@printf "\n"

.PHONY: confirm
confirm:
	@echo -n 'Are you sure? [y/N] ' && read ans && [ $${ans:-N} = y ]

.PHONY: no-dirty
no-dirty:
	@test -z "$(shell git status --porcelain)" || { echo "working tree is dirty"; exit 1; }

.PHONY: tools
tools: ## Restore local .NET tools (from .config/dotnet-tools.json if present)
	@if [ -f .config/dotnet-tools.json ]; then $(DOTNET) tool restore; else echo "no .config/dotnet-tools.json — skipping"; fi

# ==================================================================================== #
# BUILD
# ==================================================================================== #

.PHONY: restore
restore: ## Restore NuGet packages for the solution
	$(DOTNET) restore $(SLN)

.PHONY: build
build: restore ## Build the full solution ($(CONFIG))
	$(DOTNET) build $(SLN) -c $(CONFIG) --no-restore

.PHONY: rebuild
rebuild: clean build ## Clean and build from scratch

.PHONY: publish
publish: ## Publish $(PROJECT) self-contained to $(PUBLISH_DIR) for RID=$(RID)
	rm -rf $(PUBLISH_DIR)
	$(DOTNET) publish $(PROJECT) \
	  -c $(CONFIG) \
	  -r $(RID) \
	  --self-contained true \
	  -p:PublishSingleFile=true \
	  -o $(PUBLISH_DIR)

# ==================================================================================== #
# RUN
# ==================================================================================== #

.PHONY: run
run: export ASPNETCORE_ENVIRONMENT := Development
run: export ASPNETCORE_URLS := http://+:$(PORT)
run: ## Run the API on :$(PORT) with Development config
	$(DOTNET) run --project $(PROJECT) -c $(CONFIG)

.PHONY: watch
watch: export ASPNETCORE_ENVIRONMENT := Development
watch: export ASPNETCORE_URLS := http://+:$(PORT)
watch: ## Run the API with hot-reload (dotnet watch)
	$(DOTNET) watch --project $(PROJECT) run

.PHONY: kill
kill: ## Kill whatever is listening on :$(PORT)
	@pids=$$(lsof -t -i:$(PORT) 2>/dev/null || true); \
	if [ -n "$$pids" ]; then kill $$pids && echo "killed $$pids"; else echo "nothing on :$(PORT)"; fi

# ==================================================================================== #
# QUALITY CONTROL
# ==================================================================================== #

.PHONY: fmt
fmt: ## Format the solution (writes files)
	$(DOTNET) format $(SLN)

.PHONY: fmt/check
fmt/check: ## Verify formatting without writing (CI gate)
	$(DOTNET) format $(SLN) --verify-no-changes

.PHONY: test
test: ## Run ALL tests (requires Docker for integration tests)
	$(DOTNET) test $(SLN) -c $(CONFIG)

.PHONY: test/unit
test/unit: ## Run unit tests only
	$(DOTNET) test $(TESTS_UNIT) -c $(CONFIG)

.PHONY: test/integration
test/integration: ## Run integration tests (requires Docker)
	$(DOTNET) test $(TESTS_INTEGRATION) -c $(CONFIG)

.PHONY: test/architecture
test/architecture: ## Run hexagonal architecture tests
	$(DOTNET) test $(TESTS_ARCHITECTURE) -c $(CONFIG) --filter "Category=Architecture"

.PHONY: test/coverage
test/coverage: ## Run tests with coverage collection into $(COVERAGE_DIR)
	rm -rf $(COVERAGE_DIR)
	$(DOTNET) test $(SLN) -c $(CONFIG) \
	  --collect:"XPlat Code Coverage" \
	  --results-directory $(COVERAGE_DIR)
	@echo "Coverage files written under $(COVERAGE_DIR)/"

.PHONY: check
check: fmt/check build test/unit test/architecture ## Local CI gate (fast — no integration/Docker)

.PHONY: audit
audit: fmt/check build test ## Full audit including integration tests (requires Docker)

# ==================================================================================== #
# DATABASE (EF Core)
# ==================================================================================== #

.PHONY: db/migrate
db/migrate: tools ## Apply EF Core migrations to the current database
	$(DOTNET) ef database update \
	  --project $(MIGRATIONS_PROJECT) \
	  --startup-project $(STARTUP_PROJECT)

.PHONY: db/add
db/add: tools ## Add EF Core migration: make db/add NAME=<migration-name>
	@test -n "$(NAME)" || { echo "NAME=<migration-name> required"; exit 1; }
	$(DOTNET) ef migrations add $(NAME) \
	  --project $(MIGRATIONS_PROJECT) \
	  --startup-project $(STARTUP_PROJECT)

.PHONY: db/remove
db/remove: tools ## Remove the most recent EF Core migration
	$(DOTNET) ef migrations remove \
	  --project $(MIGRATIONS_PROJECT) \
	  --startup-project $(STARTUP_PROJECT)

.PHONY: db/reset
db/reset: tools confirm ## Drop and re-apply all migrations (DEV ONLY — destructive)
	$(DOTNET) ef database drop --force \
	  --project $(MIGRATIONS_PROJECT) \
	  --startup-project $(STARTUP_PROJECT)
	$(MAKE) db/migrate

# ==================================================================================== #
# DOCKER
# ==================================================================================== #

.PHONY: docker/build
docker/build: ## Build container image $(IMAGE):$(TAG) (+ :latest)
	$(DOCKER) build -t $(IMAGE):$(TAG) -t $(IMAGE):latest .

.PHONY: docker/run
docker/run: ## Run $(IMAGE):$(TAG) locally on :$(PORT)
	$(DOCKER) run --rm -it -p $(PORT):8080 $(IMAGE):$(TAG)

.PHONY: docker/push
docker/push: docker/build confirm ## Push $(IMAGE):$(TAG) to registry
	$(DOCKER) push $(IMAGE):$(TAG)
	$(DOCKER) push $(IMAGE):latest

# ==================================================================================== #
# CLEAN
# ==================================================================================== #

.PHONY: clean
clean: ## Remove build artifacts (bin/, obj/, publish/, coverage/, TestResults/)
	$(DOTNET) clean $(SLN) || true
	find . -type d \( -name bin -o -name obj -o -name TestResults \) -prune -exec rm -rf {} +
	rm -rf "$(PUBLISH_DIR)" "$(COVERAGE_DIR)"
