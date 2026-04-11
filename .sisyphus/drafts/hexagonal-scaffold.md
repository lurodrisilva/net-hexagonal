# Draft: .NET 10 Hexagonal Architecture Scaffold

## Requirements (confirmed)

- **Framework**: .NET 10 (ASP.NET Core 10)
- **Architecture**: Hexagonal (Ports & Adapters), baseada no skill `dotnet-clean-arch` e projeto de referência `dotnet-clean-arch`
- **Tipo**: Scaffold / Template de projeto canônico para microserviços
- **Deploy target**: Azure AKS (Kubernetes)

## Arquitetura (do Mermaid)

### Adaptadores de Entrada (Inbound Ports)
- **REST API Endpoint** (ASP.NET Core / FastEndpoints)
- **Consumidor Kafka** (Confluent.Kafka, IHostedService)

### Core de Negócios
- **Camada de Aplicação**: Casos de uso / CQRS (Commands + Queries via Mediator)
- **Camada de Domínio**: Entidades, Value Objects, Políticas, Domain Events

### Adaptadores de Saída (Outbound Ports)
- **Cliente REST API** (HttpClientFactory + Microsoft.Extensions.Http.Resilience)
- **Produtor Kafka** (Confluent.Kafka)

### Adaptadores de Dados (Data Ports)
- **Repositório SQL**: PostgreSQL (Npgsql + EF Core)
- **Repositório NoSQL**: Azure DocumentDB / MongoDB (MongoDB.Driver)
- **Repositório Cache**: Azure Managed Redis (StackExchange.Redis)

### Serviços Azure
- Azure Database for PostgreSQL
- Azure DocumentDB (Cosmos DB MongoDB API?)
- Azure Managed Redis

## SDKs Obrigatórios (confirmed)
- ASP.NET Core 10
- Microsoft.AspNetCore.OpenApi
- Microsoft.Extensions.Diagnostics.HealthChecks
- Microsoft.AspNetCore.RateLimiting
- Azure.Identity
- Confluent.Kafka
- Npgsql + Npgsql.EntityFrameworkCore.PostgreSQL
- MongoDB.Driver
- StackExchange.Redis
- OpenTelemetry (Hosting, ASP.NET Core, HTTP, Runtime, OTLP Exporter)
- Microsoft.Extensions.Http.Resilience

## SDKs Opcionais (confirmed)
- Dapper (leitura pesada SQL)
- Quartz.NET (batch/scheduler)
- FluentValidation (validação de fronteira)
- Scrutor (DI scanning/decoration)
- Hellang.Middleware.ProblemDetails (padronização de erros)

## User Decisions (from interview)

- **Nome da Solução**: `Hex.Scaffold`
- **Entidade de Exemplo**: SIM — aggregate completo demonstrando CRUD, Kafka, Redis, Mongo e Postgres
- **SDKs Opcionais**: TODOS exceto Quartz.NET (será projeto separado)
  - Dapper, FluentValidation, Scrutor, ProblemDetails
- **DevOps**: Apenas Dockerfile (multi-stage)
- **Testes**: Unit + Integration + Architecture (xUnit, TestContainers, NetArchTest)

## Technical Decisions

- **Solution name**: `Hex.Scaffold` → namespaces `Hex.Scaffold.*`
- **Target framework**: net10.0 (TreatWarningsAsErrors, nullable, implicit usings)
- **Central Package Management**: Directory.Packages.props (padrão da referência)
- **Mediator**: Mediator com source generator (padrão da referência)
- **Value Objects**: Vogen para single-value, ValueObject base para composites
- **Result Pattern**: Ardalis.Result para error handling
- **Specifications**: Ardalis.Specification para queries
- **Endpoints**: FastEndpoints (REPR pattern, padrão da referência)
- **Domain Events**: EF Core SaveChangesInterceptor + MediatorDomainEventDispatcher
- **Logging**: Serilog + OpenTelemetry
- **Health Checks**: Liveness + Readiness por adaptador (Postgres, Mongo, Redis, Kafka)
- **HTTP Resilience**: Standard resilience handler com retry, circuit breaker, timeout

## Research Findings

### Projeto de Referência (dotnet-clean-arch)
- **Estrutura**: Core → UseCases → Infrastructure → Web (4 camadas)
- **net10.0** com Directory.Build.props e Directory.Packages.props centralizados
- **Aspire integration**: ServiceDefaults com OpenTelemetry, Health Checks, HTTP Resilience
- **EF Core**: AppDbContext com automatic config discovery, EventDispatcherInterceptor
- **FastEndpoints**: REPR pattern com Results<>, Validators, Mappers
- **Mediator**: Source-generated com pipeline behaviors (LoggingBehavior)
- **DI**: Extension methods por camada (AddInfrastructureServices, AddMediatorSourceGen)
- **Testes**: UnitTests (NSubstitute), IntegrationTests (InMemory DB), FunctionalTests (TestContainers)

### Mapeamento Clean Arch → Hexagonal (do diagrama)
- Clean.Core → Hex.Scaffold.Domain (entidades, VOs, eventos, policies, port interfaces)
- Clean.UseCases → Hex.Scaffold.Application (CQRS, DTOs, application services)
- Clean.Infrastructure → Split into 3 projetos de adaptadores:
  - Hex.Scaffold.Adapters.Inbound (REST API endpoints, Kafka consumers)
  - Hex.Scaffold.Adapters.Outbound (REST clients, Kafka producers)
  - Hex.Scaffold.Adapters.Persistence (EF Core/PostgreSQL, MongoDB, Redis)
- Clean.Web → Hex.Scaffold.Api (Host/Program.cs, configurations, middleware)

### SDKs Integration Patterns
- **Kafka**: Producer wrapper (IKafkaProducer) + Consumer BackgroundService (IHostedService)
- **OpenTelemetry**: Extension method com Traces, Metrics, Logs via OTLP
- **MongoDB**: Generic repository + IMongoClient singleton + health check
- **Redis**: IConnectionMultiplexer singleton + ICacheService + health check
- **HTTP Resilience**: AddStandardResilienceHandler() com retry/circuit breaker/timeout
- **PostgreSQL**: EF Core com Npgsql, EnableRetryOnFailure, migration health check

## Scope Boundaries

### INCLUDE
- Scaffold completo: 7 projetos (Domain, Application, Adapters.Inbound, Adapters.Outbound, Adapters.Persistence, Api, Tests)
- Entidade de exemplo (Sample) end-to-end: Domain → Application → todos os adaptadores
- Dockerfile multi-stage otimizado para .NET 10
- Todos SDKs obrigatórios + opcionais (exceto Quartz.NET) configurados
- Health checks por adaptador (PostgreSQL, MongoDB, Redis)
- OpenTelemetry completo (traces, metrics, logs)
- Projetos de teste: Unit, Integration, Architecture
- Rate limiting, OpenAPI documentation
- ProblemDetails para padronização de erros

### EXCLUDE
- Quartz.NET (projeto separado)
- Kubernetes manifests / Helm charts
- CI/CD pipelines
- Aspire integration (scaffold standalone, sem AppHost/ServiceDefaults)
- Real authentication/authorization implementation (placeholder apenas)

## Test Strategy Decision
- **Infrastructure exists**: NO (projeto novo)
- **Automated tests**: YES (tests-after — scaffold cria projetos de teste com exemplos)
- **Framework**: xUnit + NSubstitute + Shouldly + TestContainers + NetArchTest
- **Agent-Executed QA**: ALWAYS (build, dotnet test, architecture compliance)
