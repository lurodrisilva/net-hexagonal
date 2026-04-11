---
name: dotnet-clean-arch
description: MUST USE when working on .NET Clean Architecture projects (Ardalis template). Guides implementation of aggregates, value objects, CQRS use cases, FastEndpoints API, and infrastructure following strict layering rules with dependency inversion. Covers domain events, specifications, Vogen value objects, Mediator handlers, EF Core configuration, and DI registration.
license: MIT
compatibility: opencode
metadata:
  framework: dotnet
  pattern: clean-architecture
  template: ardalis
---

# .NET Clean Architecture Skill

You are a .NET Clean Architecture expert. You build features following strict layering, DDD patterns, and CQRS — matching the conventions established in the Ardalis Clean Architecture template.

---

## ARCHITECTURE RULES (NON-NEGOTIABLE)

```
                    ┌─────────┐
                    │  Core   │  Domain Model — ZERO infrastructure dependencies
                    └────▲────┘
                         │
                  ┌──────┴──────┐
                  │  UseCases   │  CQRS Commands + Queries via Mediator
                  └──────▲──────┘
                         │
              ┌──────────┴──────────┐
              │   Infrastructure    │  EF Core, Email, External Services
              └──────────▲──────────┘
                         │
                    ┌────┴────┐
                    │   Web   │  FastEndpoints API (REPR pattern)
                    └─────────┘
```

### Dependency Law

| Project | Can Reference | MUST NEVER Reference |
|---------|---------------|----------------------|
| **Core** | Nothing (only NuGet: SharedKernel, Vogen, GuardClauses, Specification, SmartEnum, Mediator.Abstractions) | UseCases, Infrastructure, Web |
| **UseCases** | Core | Infrastructure, Web |
| **Infrastructure** | Core, UseCases | Web |
| **Web** | Infrastructure, UseCases, ServiceDefaults | — |

**VIOLATION = AUTOMATIC FAILURE. If you need something from an outer layer in an inner layer, define an interface in Core and implement it in Infrastructure.**

---

## MODE DETECTION (FIRST STEP)

Analyze the user's request to determine what to build:

| User Request Pattern | Mode | Jump To |
|---------------------|------|---------|
| "add entity", "new aggregate", "domain model" | `NEW_AGGREGATE` | Phase 1 |
| "add command", "add query", "new use case", "CQRS" | `NEW_USE_CASE` | Phase 2 |
| "add endpoint", "new API", "new route" | `NEW_ENDPOINT` | Phase 3 |
| "add feature" (end-to-end) | `FULL_FEATURE` | Phase 1 → 2 → 3 → 4 → 5 |
| "add value object", "new VO" | `VALUE_OBJECT` | Phase 1.2 |
| "add domain event" | `DOMAIN_EVENT` | Phase 1.4 |
| "fix", "update", "change behavior" | `MODIFY` | Assess scope first |

**For FULL_FEATURE**: Execute all phases in order. Create TODO list immediately.

---

## PHASE 1: DOMAIN MODEL (Core Project)

### File Location

```
src/{Project}.Core/
  {AggregateName}Aggregate/
    {AggregateName}.cs              ← Aggregate Root entity
    {AggregateName}Id.cs            ← Strongly-typed ID (Vogen)
    {ValueObject}.cs                ← Value Objects
    {SmartEnum}.cs                  ← Smart Enums
    Events/
      {Event}Event.cs               ← Domain Events
    Handlers/
      {Handler}Handler.cs           ← Domain Event Handlers
    Specifications/
      {Spec}Spec.cs                 ← Query Specifications
  Interfaces/
    I{Service}.cs                   ← Service interfaces
  Services/
    {Service}.cs                    ← Domain Services
```

### 1.1 Aggregate Root Entity

```csharp
using {Project}.Core.{Aggregate}Aggregate.Events;

namespace {Project}.Core.{Aggregate}Aggregate;

public class {Name}({RequiredValueObject} {prop}) : EntityBase<{Name}, {Name}Id>, IAggregateRoot
{
  public {RequiredValueObject} {Prop} { get; private set; } = {prop};
  public {OptionalType}? {OptionalProp} { get; private set; }

  // Mutation methods — return 'this' for fluent API
  public {Name} Update{Prop}({ValueObject} new{Prop})
  {
    if ({Prop} == new{Prop}) return this;
    {Prop} = new{Prop};
    RegisterDomainEvent(new {Name}{Prop}UpdatedEvent(this));
    return this;
  }
}
```

**RULES:**
- Primary constructor for REQUIRED properties only
- ALL setters `private set`
- Mutations through methods, never direct property access
- Register domain events inside mutation methods when side effects needed
- Return `this` from mutation methods for fluent chaining
- Use `IAggregateRoot` marker interface
- Inherit from `EntityBase<TSelf, TId>`

### 1.2 Strongly-Typed ID (Vogen)

```csharp
using Vogen;

namespace {Project}.Core.{Aggregate}Aggregate;

[ValueObject<int>]
public readonly partial struct {Name}Id
{
  private static Validation Validate(int value)
    => value > 0 ? Validation.Ok : Validation.Invalid("{Name}Id must be positive.");
}
```

**RULES:**
- Always `readonly partial struct`
- Always `[ValueObject<int>]` (or the underlying primitive type)
- Always validate > 0 for IDs
- The `[assembly: VogenDefaults(...)]` attribute is already configured in the project

### 1.3 Value Objects

**Option A: Vogen (for single-value wrappers — PREFERRED)**

```csharp
using Vogen;

namespace {Project}.Core.{Aggregate}Aggregate;

[ValueObject<string>(conversions: Conversions.SystemTextJson)]
public partial struct {Name}
{
  public const int MaxLength = 100;
  private static Validation Validate(in string value) =>
    string.IsNullOrEmpty(value)
      ? Validation.Invalid("{Name} cannot be empty")
      : value.Length > MaxLength
        ? Validation.Invalid($"{Name} cannot be longer than {MaxLength} characters")
        : Validation.Ok;
}
```

**Option B: Manual ValueObject (for multi-property composites)**

```csharp
namespace {Project}.Core.{Aggregate}Aggregate;

public class {Name}({type1} {prop1}, {type2} {prop2}) : ValueObject
{
  public {type1} {Prop1} { get; private set; } = {prop1};
  public {type2} {Prop2} { get; private set; } = {prop2};

  protected override IEnumerable<object> GetEqualityComponents()
  {
    yield return {Prop1};
    yield return {Prop2};
  }
}
```

**DECISION TABLE:**

| Scenario | Use |
|----------|-----|
| Wraps a single primitive (string, int, decimal) | Vogen `[ValueObject<T>]` |
| Multiple properties (e.g., Address, PhoneNumber) | Manual `ValueObject` base class |
| Enum-like with behavior | `SmartEnum<T>` |

### 1.4 Smart Enums

```csharp
namespace {Project}.Core.{Aggregate}Aggregate;

public class {Name}Status : SmartEnum<{Name}Status>
{
  public static readonly {Name}Status Active = new(nameof(Active), 1);
  public static readonly {Name}Status Inactive = new(nameof(Inactive), 2);
  public static readonly {Name}Status NotSet = new(nameof(NotSet), 3);

  protected {Name}Status(string name, int value) : base(name, value) { }
}
```

### 1.5 Domain Events

```csharp
namespace {Project}.Core.{Aggregate}Aggregate.Events;

public sealed class {Name}{Action}Event({PayloadType} {payload}) : DomainEventBase
{
  public {PayloadType} {Payload} { get; init; } = {payload};
}
```

**RULES:**
- Always `sealed class`
- Primary constructor with payload
- `init` properties for immutability
- Naming: `{Entity}{Action}Event` (e.g., `ContributorNameUpdatedEvent`, `OrderCompletedEvent`)
- Placed in `Events/` folder inside aggregate

### 1.6 Domain Event Handlers

```csharp
using {Project}.Core.{Aggregate}Aggregate.Events;
using {Project}.Core.Interfaces;

namespace {Project}.Core.{Aggregate}Aggregate.Handlers;

public class {EventName}Handler(
  ILogger<{EventName}Handler> logger,
  IEmailSender emailSender) : INotificationHandler<{EventName}>
{
  public async ValueTask Handle({EventName} domainEvent, CancellationToken cancellationToken)
  {
    logger.LogInformation("Handling {Event} for {Id}", nameof({EventName}), domainEvent.{IdProp});
    // Side effects: email, logging, cross-aggregate updates, etc.
  }
}
```

**RULES:**
- Handlers live in Core (they handle domain events, they ARE domain logic)
- Dependencies injected via primary constructor
- Infrastructure interfaces (IEmailSender) defined in `Core/Interfaces/`
- Use `INotificationHandler<TEvent>` from Mediator
- Return `ValueTask` (not `Task`)

### 1.7 Specifications

```csharp
namespace {Project}.Core.{Aggregate}Aggregate.Specifications;

public class {Name}ByIdSpec : Specification<{AggregateRoot}>
{
  public {Name}ByIdSpec({Name}Id id) =>
    Query.Where(x => x.Id == id);
}

// With includes:
public class {Name}WithItemsSpec : Specification<{AggregateRoot}>
{
  public {Name}WithItemsSpec({Name}Id id) =>
    Query
      .Where(x => x.Id == id)
      .Include(x => x.Items);
}
```

### 1.8 Domain Service Interfaces (Core/Interfaces/)

```csharp
namespace {Project}.Core.Interfaces;

public interface I{Action}{Name}Service
{
  public ValueTask<Result> {Action}{Name}({Name}Id id);
}
```

### 1.9 Domain Services (Core/Services/)

```csharp
namespace {Project}.Core.Services;

public class {Action}{Name}Service(
  IRepository<{Name}> _repository,
  IMediator _mediator,
  ILogger<{Action}{Name}Service> _logger) : I{Action}{Name}Service
{
  public async ValueTask<Result> {Action}{Name}({Name}Id id)
  {
    _logger.LogInformation("{Action} {Name} {id}", id);
    var entity = await _repository.GetByIdAsync(id);
    if (entity == null) return Result.NotFound();

    await _repository.DeleteAsync(entity);
    var domainEvent = new {Name}{Action}Event(id);
    await _mediator.Publish(domainEvent);

    return Result.Success();
  }
}
```

**WHEN to use Domain Services:**
- Need to publish domain events after deletion (entities can't fire events after they're deleted)
- Cross-aggregate coordination
- Complex business logic spanning multiple repositories

---

## PHASE 2: USE CASES (UseCases Project)

### File Location

```
src/{Project}.UseCases/
  {Feature}/
    {Feature}DTO.cs                            ← Shared DTO
    Create/
      Create{Feature}Command.cs                ← Command record
      Create{Feature}Handler.cs                ← Handler
    Update/
      Update{Feature}Command.cs
      Update{Feature}Handler.cs
    Delete/
      Delete{Feature}Command.cs
      Delete{Feature}Handler.cs
    Get/
      Get{Feature}Query.cs                     ← Query record
      Get{Feature}Handler.cs
    List/
      List{Feature}Query.cs
      List{Feature}Handler.cs
      IList{Feature}QueryService.cs            ← Query service interface
```

### 2.1 DTO

```csharp
using {Project}.Core.{Aggregate}Aggregate;

namespace {Project}.UseCases.{Feature};

public record {Feature}Dto({IdType} Id, {NameType} Name, {OtherType} Other);
```

### 2.2 Command (Mutation)

```csharp
using {Project}.Core.{Aggregate}Aggregate;

namespace {Project}.UseCases.{Feature}.Create;

public record Create{Feature}Command({ValueObject} Name, string? OptionalProp)
  : ICommand<Result<{Feature}Id>>;
```

### 2.3 Command Handler

```csharp
using {Project}.Core.{Aggregate}Aggregate;

namespace {Project}.UseCases.{Feature}.Create;

public class Create{Feature}Handler(IRepository<{Entity}> _repository)
  : ICommandHandler<Create{Feature}Command, Result<{Feature}Id>>
{
  public async ValueTask<Result<{Feature}Id>> Handle(
    Create{Feature}Command command,
    CancellationToken cancellationToken)
  {
    var entity = new {Entity}(command.Name);
    var created = await _repository.AddAsync(entity, cancellationToken);
    return created.Id;
  }
}
```

### 2.4 Query (Read)

```csharp
using {Project}.Core.{Aggregate}Aggregate;

namespace {Project}.UseCases.{Feature}.Get;

public record Get{Feature}Query({Feature}Id {Feature}Id)
  : IQuery<Result<{Feature}Dto>>;
```

### 2.5 Query Handler (via Repository + Specification)

```csharp
using {Project}.Core.{Aggregate}Aggregate;
using {Project}.Core.{Aggregate}Aggregate.Specifications;

namespace {Project}.UseCases.{Feature}.Get;

public class Get{Feature}Handler(IReadRepository<{Entity}> _repository)
  : IQueryHandler<Get{Feature}Query, Result<{Feature}Dto>>
{
  public async ValueTask<Result<{Feature}Dto>> Handle(
    Get{Feature}Query request, CancellationToken cancellationToken)
  {
    var spec = new {Feature}ByIdSpec(request.{Feature}Id);
    var entity = await _repository.FirstOrDefaultAsync(spec, cancellationToken);
    if (entity == null) return Result.NotFound();

    return new {Feature}Dto(entity.Id, entity.Name, /* map other props */);
  }
}
```

### 2.6 List Query (via Query Service — bypasses repository for performance)

```csharp
namespace {Project}.UseCases.{Feature}.List;

public record List{Feature}Query(int? Page = 1, int? PerPage = Constants.DEFAULT_PAGE_SIZE)
  : IQuery<Result<PagedResult<{Feature}Dto>>>;
```

```csharp
namespace {Project}.UseCases.{Feature}.List;

public interface IList{Feature}QueryService
{
  Task<PagedResult<{Feature}Dto>> ListAsync(int page, int perPage);
}
```

```csharp
namespace {Project}.UseCases.{Feature}.List;

public class List{Feature}Handler(IList{Feature}QueryService _query)
  : IQueryHandler<List{Feature}Query, Result<PagedResult<{Feature}Dto>>>
{
  public async ValueTask<Result<PagedResult<{Feature}Dto>>> Handle(
    List{Feature}Query request, CancellationToken cancellationToken)
  {
    var result = await _query.ListAsync(
      request.Page ?? 1,
      request.PerPage ?? Constants.DEFAULT_PAGE_SIZE);
    return Result.Success(result);
  }
}
```

### 2.7 Delete Command (via Domain Service)

```csharp
namespace {Project}.UseCases.{Feature}.Delete;

public record Delete{Feature}Command({Feature}Id {Feature}Id) : ICommand<Result>;
```

```csharp
using {Project}.Core.Interfaces;

namespace {Project}.UseCases.{Feature}.Delete;

public class Delete{Feature}Handler(I{Delete}{Feature}Service _service)
  : ICommandHandler<Delete{Feature}Command, Result>
{
  public async ValueTask<Result> Handle(
    Delete{Feature}Command request, CancellationToken cancellationToken) =>
    await _service.Delete{Feature}(request.{Feature}Id);
}
```

**CQRS RULES:**

| Type | Interface | Returns | Data Access |
|------|-----------|---------|-------------|
| Command | `ICommand<Result<T>>` / `ICommandHandler<,>` | `Result<T>` | `IRepository<T>` (read/write) |
| Query | `IQuery<Result<T>>` / `IQueryHandler<,>` | `Result<T>` | `IReadRepository<T>` or `IQueryService` |

- Commands MUTATE state — use `IRepository<T>`
- Queries are READONLY — can use `IReadRepository<T>`, specifications, or dedicated query services
- **Queries MAY bypass repository** for performance (raw SQL, Dapper, etc.)
- ALL handlers return `Result<T>` — never throw exceptions for expected failures
- Handler methods return `ValueTask`, not `Task`

---

## PHASE 3: API ENDPOINTS (Web Project)

### File Location (REPR Pattern)

```
src/{Project}.Web/
  {Feature}/
    {Feature}Record.cs                         ← Shared API response DTO
    Create.cs                                  ← POST endpoint
    Create.CreateRequest.cs                    ← (optional: separate file)
    Create.CreateValidator.cs                  ← (optional: separate file)
    List.cs                                    ← GET (collection)
    GetById.cs                                 ← GET (single)
    GetById.GetByIdRequest.cs
    GetById.GetByIdValidator.cs
    Update.cs                                  ← PUT
    Update.UpdateRequest.cs
    Update.UpdateResponse.cs
    Update.UpdateValidator.cs
    Delete.cs                                  ← DELETE
    Delete.DeleteRequest.cs
    Delete.DeleteValidator.cs
```

### 3.1 Shared API Record

```csharp
namespace {Project}.Web.{Feature};

public record {Feature}Record(int Id, string Name, string OtherProp);
```

### 3.2 Create Endpoint (POST)

```csharp
using {Project}.Core.{Aggregate}Aggregate;
using {Project}.UseCases.{Feature}.Create;
using {Project}.Web.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace {Project}.Web.{Feature};

public class Create(IMediator mediator)
  : Endpoint<Create{Feature}Request,
      Results<Created<Create{Feature}Response>,
              ValidationProblem,
              ProblemHttpResult>>
{
  private readonly IMediator _mediator = mediator;

  public override void Configure()
  {
    Post(Create{Feature}Request.Route);
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Create a new {feature}";
      s.Description = "Creates a new {feature} with the provided data.";
      s.ExampleRequest = new Create{Feature}Request { Name = "Example" };
      s.Responses[201] = "{Feature} created successfully";
      s.Responses[400] = "Invalid input data";
    });
    Tags("{Feature}s");
    Description(builder => builder
      .Accepts<Create{Feature}Request>("application/json")
      .Produces<Create{Feature}Response>(201, "application/json")
      .ProducesProblem(400)
      .ProducesProblem(500));
  }

  public override async Task<Results<Created<Create{Feature}Response>, ValidationProblem, ProblemHttpResult>>
    ExecuteAsync(Create{Feature}Request request, CancellationToken cancellationToken)
  {
    var result = await _mediator.Send(
      new Create{Feature}Command({ValueObject}.From(request.Name!), request.OptionalProp));

    return result.ToCreatedResult(
      id => $"/{Feature}s/{id}",
      id => new Create{Feature}Response(id.Value, request.Name!));
  }
}
```

### 3.3 Request / Response / Validator

```csharp
// Request
public class Create{Feature}Request
{
  public const string Route = "/{Feature}s";

  [Required]
  public string Name { get; set; } = string.Empty;
  public string? OptionalProp { get; set; }
}

// Validator (FluentValidation)
public class Create{Feature}Validator : Validator<Create{Feature}Request>
{
  public Create{Feature}Validator()
  {
    RuleFor(x => x.Name)
      .NotEmpty().WithMessage("Name is required.")
      .MinimumLength(2)
      .MaximumLength({ValueObject}.MaxLength);
  }
}

// Response
public class Create{Feature}Response(int id, string name)
{
  public int Id { get; set; } = id;
  public string Name { get; set; } = name;
}
```

### 3.4 GetById Endpoint (GET)

```csharp
using {Project}.Core.{Aggregate}Aggregate;
using {Project}.UseCases.{Feature};
using {Project}.UseCases.{Feature}.Get;
using {Project}.Web.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace {Project}.Web.{Feature};

public class GetById(IMediator mediator)
  : Endpoint<Get{Feature}ByIdRequest,
             Results<Ok<{Feature}Record>, NotFound, ProblemHttpResult>,
             Get{Feature}ByIdMapper>
{
  public override void Configure()
  {
    Get(Get{Feature}ByIdRequest.Route);
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Get a {feature} by ID";
      s.Responses[200] = "{Feature} found";
      s.Responses[404] = "{Feature} not found";
    });
    Tags("{Feature}s");
  }

  public override async Task<Results<Ok<{Feature}Record>, NotFound, ProblemHttpResult>>
    ExecuteAsync(Get{Feature}ByIdRequest request, CancellationToken ct)
  {
    var result = await mediator.Send(
      new Get{Feature}Query({Feature}Id.From(request.{Feature}Id)), ct);

    return result.ToGetByIdResult(Map.FromEntity);
  }
}

// Request
public class Get{Feature}ByIdRequest
{
  public const string Route = "/{Feature}s/{{{Feature}Id:int}}";
  public int {Feature}Id { get; set; }
}

// Mapper
public sealed class Get{Feature}ByIdMapper
  : Mapper<Get{Feature}ByIdRequest, {Feature}Record, {Feature}Dto>
{
  public override {Feature}Record FromEntity({Feature}Dto e)
    => new(e.Id.Value, e.Name.Value, e.OtherProp?.ToString() ?? "");
}
```

### 3.5 Delete Endpoint (DELETE)

```csharp
using {Project}.Core.{Aggregate}Aggregate;
using {Project}.UseCases.{Feature}.Delete;
using {Project}.Web.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace {Project}.Web.{Feature};

public class Delete : Endpoint<Delete{Feature}Request,
                               Results<NoContent, NotFound, ProblemHttpResult>>
{
  private readonly IMediator _mediator;
  public Delete(IMediator mediator) => _mediator = mediator;

  public override void Configure()
  {
    Delete(Delete{Feature}Request.Route);
    AllowAnonymous();
    Summary(s =>
    {
      s.Summary = "Delete a {feature}";
      s.Responses[204] = "{Feature} deleted";
      s.Responses[404] = "{Feature} not found";
    });
    Tags("{Feature}s");
  }

  public override async Task<Results<NoContent, NotFound, ProblemHttpResult>>
    ExecuteAsync(Delete{Feature}Request req, CancellationToken ct)
  {
    var cmd = new Delete{Feature}Command({Feature}Id.From(req.{Feature}Id));
    var result = await _mediator.Send(cmd, ct);
    return result.ToDeleteResult();
  }
}
```

### ENDPOINT RULES

- **One class per HTTP operation** — `Create.cs`, `GetById.cs`, `List.cs`, `Update.cs`, `Delete.cs`
- **REPR pattern**: Request → Endpoint → Response (separate types)
- **Mediator only** — endpoints NEVER access repositories directly
- **Value Object conversion at boundary** — `{VO}.From(request.Prop)` in endpoint, not in handler
- **AllowAnonymous** — default for template; add auth as needed
- **Configure()** must declare: route, auth, summary, tags, description
- **Result extension methods** — use `ToCreatedResult`, `ToGetByIdResult`, `ToUpdateResult`, `ToDeleteResult`
- **Mappers** — use `Mapper<TRequest, TResponse, TEntity>` for DTO → API record conversion

---

## PHASE 4: INFRASTRUCTURE

### 4.1 EF Core Entity Configuration

```
src/{Project}.Infrastructure/
  Data/
    Config/
      {Entity}Configuration.cs          ← EF Core mapping
      VogenEfCoreConverters.cs           ← Value converters for Vogen types
    Queries/
      List{Feature}QueryService.cs       ← Query service implementation
    AppDbContext.cs                       ← Add DbSet
    EfRepository.cs                      ← Already generic (no changes needed)
  Email/
    ...
  InfrastructureServiceExtensions.cs     ← DI registration
```

**AppDbContext — Add DbSet:**

```csharp
public DbSet<{Entity}> {Entity}s => Set<{Entity}>();
```

**Entity Configuration:**

```csharp
using {Project}.Core.{Aggregate}Aggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace {Project}.Infrastructure.Data.Config;

public class {Entity}Configuration : IEntityTypeConfiguration<{Entity}>
{
  public void Configure(EntityTypeBuilder<{Entity}> builder)
  {
    builder.HasKey(x => x.Id);

    // Vogen ID conversion
    builder.Property(x => x.Id)
      .HasConversion(
        id => id.Value,
        value => {Entity}Id.From(value));

    // Vogen string value object conversion
    builder.Property(x => x.Name)
      .HasMaxLength({EntityName}.MaxLength)
      .HasConversion(
        name => name.Value,
        value => {EntityName}.From(value));

    // Owned entity for composite value objects
    builder.OwnsOne(x => x.PhoneNumber, phone =>
    {
      phone.Property(p => p.CountryCode).HasColumnName("PhoneNumber_CountryCode");
      phone.Property(p => p.Number).HasColumnName("PhoneNumber_Number");
      phone.Property(p => p.Extension).HasColumnName("PhoneNumber_Extension");
    });

    // SmartEnum conversion
    builder.Property(x => x.Status)
      .HasConversion(
        status => status.Value,
        value => {Entity}Status.FromValue(value));
  }
}
```

### 4.2 Query Service Implementation

```csharp
using {Project}.Core.{Aggregate}Aggregate;
using {Project}.UseCases.{Feature};
using {Project}.UseCases.{Feature}.List;

namespace {Project}.Infrastructure.Data.Queries;

public class List{Feature}QueryService(AppDbContext _db) : IList{Feature}QueryService
{
  public async Task<PagedResult<{Feature}Dto>> ListAsync(int page, int perPage)
  {
    var items = await _db.{Entity}s
      .OrderBy(x => x.Id)
      .Skip((page - 1) * perPage)
      .Take(perPage)
      .Select(x => new {Feature}Dto(x.Id, x.Name, /* other props */))
      .AsNoTracking()
      .ToListAsync();

    int totalCount = await _db.{Entity}s.CountAsync();
    int totalPages = (int)Math.Ceiling(totalCount / (double)perPage);

    return new PagedResult<{Feature}Dto>(items, page, perPage, totalCount, totalPages);
  }
}
```

### 4.3 DI Registration

**Add to `InfrastructureServiceExtensions.AddInfrastructureServices()`:**

```csharp
services.AddScoped<IList{Feature}QueryService, List{Feature}QueryService>()
        .AddScoped<IDelete{Feature}Service, Delete{Feature}Service>();
```

**NOTE:** `IRepository<>` and `IReadRepository<>` are registered as OPEN GENERICS — no per-entity registration needed.

### 4.4 EF Core Migration

```bash
# From the Web project directory:
dotnet ef migrations add Add{Feature}Entity -c AppDbContext \
  -p ../Clean.Architecture.Infrastructure/Clean.Architecture.Infrastructure.csproj \
  -s Clean.Architecture.Web.csproj \
  -o Data/Migrations
```

### 4.5 Mediator Registration

**If adding a new assembly**, add a representative type to `MediatorConfig.cs`:

```csharp
options.Assemblies =
[
  typeof(Contributor),                       // Core
  typeof(CreateContributorCommand),          // UseCases
  typeof(InfrastructureServiceExtensions),   // Infrastructure
  typeof(MediatorConfig)                     // Web
];
```

New handlers/events within existing assemblies are auto-discovered. No changes needed.

---

## PHASE 5: VERIFICATION CHECKLIST

### After Every Feature Implementation

```
VERIFICATION CHECKLIST
======================

ARCHITECTURE:
  [ ] Core has NO references to outer layers
  [ ] UseCases has NO references to Infrastructure or Web
  [ ] Interfaces defined in Core, implemented in Infrastructure
  [ ] DI registration in InfrastructureServiceExtensions or ServiceConfigs

DOMAIN MODEL:
  [ ] Entity has private setters
  [ ] Mutations through methods only
  [ ] Value objects validated at construction (Vogen)
  [ ] Domain events registered in mutation methods
  [ ] Specification created for common queries

USE CASES:
  [ ] Commands use IRepository<T> (read/write)
  [ ] Queries use IReadRepository<T> or IQueryService (read-only)
  [ ] All handlers return Result<T>
  [ ] Handler methods return ValueTask

ENDPOINTS:
  [ ] One endpoint per HTTP operation
  [ ] Request validation via FluentValidation
  [ ] Value Object conversion at API boundary
  [ ] Endpoints use Mediator.Send(), never repositories
  [ ] Summary + Tags configured for Swagger

INFRASTRUCTURE:
  [ ] EF Core configuration for entity + value objects
  [ ] Vogen value converters configured
  [ ] Query service registered in DI
  [ ] Domain service registered in DI

CODING STYLE:
  [ ] 2-space indentation, Allman braces, file-scoped namespaces
  [ ] Private fields prefixed with _, explicit visibility on all members
  [ ] PascalCase constants, language keywords (int not Int32)
  [ ] nameof() used instead of string literals for identifiers
  [ ] var only when type is obvious from right-hand side
  [ ] sealed/static on private/internal types
  [ ] Records for immutable types (commands, queries, DTOs, events)
  [ ] Primary constructors with _ prefix on handler dependencies

PERFORMANCE:
  [ ] Read queries use AsNoTracking() or .Select() projection
  [ ] No N+1 queries — Include() in specs or projection in query services
  [ ] CancellationToken forwarded through all async calls
  [ ] List endpoints paginated (Skip/Take, bounded page size)
  [ ] Structured logging templates, not string interpolation
  [ ] No unnecessary async state machines on trivial pass-throughs
  [ ] No unbounded result sets or caches

BUILD:
  [ ] dotnet build succeeds with zero warnings
  [ ] LSP diagnostics clean on all changed files
```

Run `dotnet build {SolutionFile}` after changes. **Warnings are errors** (TreatWarningsAsErrors is enabled).

---

## FULL FEATURE CHECKLIST (End-to-End)

When adding a complete new feature/aggregate, create these files IN ORDER:

```
STEP  LAYER           FILE                                          DEPENDS ON
────  ──────────────  ──────────────────────────────────────────    ──────────
 1    Core            {Name}Aggregate/{Name}Id.cs                   —
 2    Core            {Name}Aggregate/{Name}{Prop}.cs (VOs)         —
 3    Core            {Name}Aggregate/{Name}Status.cs (enum)        —
 4    Core            {Name}Aggregate/{Name}.cs (entity)            Steps 1-3
 5    Core            {Name}Aggregate/Events/*.cs                   Step 4
 6    Core            {Name}Aggregate/Handlers/*.cs                 Step 5
 7    Core            {Name}Aggregate/Specifications/*.cs           Step 4
 8    Core            Interfaces/IDelete{Name}Service.cs            Step 1
 9    Core            Services/Delete{Name}Service.cs               Steps 4,5,8
10    UseCases        {Name}/{Name}DTO.cs                           Steps 1-3
11    UseCases        {Name}/Create/Command + Handler               Step 4
12    UseCases        {Name}/Get/Query + Handler                    Steps 7,10
13    UseCases        {Name}/List/Query + Handler + IQueryService   Step 10
14    UseCases        {Name}/Update/Command + Handler               Step 4
15    UseCases        {Name}/Delete/Command + Handler               Step 9
16    Infra           Data/Config/{Name}Configuration.cs            Steps 1-4
17    Infra           Data/AppDbContext.cs (add DbSet)              Step 4
18    Infra           Data/Queries/List{Name}QueryService.cs        Step 13
19    Infra           InfrastructureServiceExtensions.cs (DI)       Steps 13,8
20    Web             {Name}/{Name}Record.cs                        —
21    Web             {Name}/Create.cs + Request + Validator        Step 11
22    Web             {Name}/GetById.cs + Request + Mapper          Step 12
23    Web             {Name}/List.cs + Request + Validator + Mapper Step 13
24    Web             {Name}/Update.cs + Request + Validator + Mapper Step 14
25    Web             {Name}/Delete.cs + Request + Validator        Step 15
26    Infra           EF Migration                                  Steps 16-17
27    ALL             dotnet build + verify                         All above
```

---

## ANTI-PATTERNS (AUTOMATIC FAILURE)

| Anti-Pattern | Why It's Wrong | Correct Approach |
|---|---|---|
| Public setters on entities | Breaks encapsulation | Private setters + mutation methods |
| Repositories in endpoints | Endpoint should not know data access | Use Mediator.Send(command/query) |
| Infrastructure types in Core | Violates dependency rule | Define interface in Core, implement in Infrastructure |
| Throwing exceptions for expected failures | Expensive, hard to handle | Return `Result.NotFound()`, `Result.Invalid()` |
| `as any` / `#pragma warning disable` | Hides real issues | Fix the actual type problem |
| Mutable value objects | Defeats the purpose | Use Vogen or override `GetEqualityComponents()` |
| Command handlers reading data for API responses | Commands mutate, not read | Return only the ID; query separately if needed |
| Domain events in UseCases layer | Events are domain logic | Keep in Core aggregate Events/ folder |
| Direct `DbContext` in UseCases | Leaks infrastructure | Use `IRepository<T>` or `IQueryService` |
| One endpoint class with multiple HTTP methods | Violates REPR pattern | One class per HTTP verb |
| `Task` instead of `ValueTask` in handlers | Mediator expects ValueTask | Use `ValueTask` for all handlers |

---

## C# CODING STYLE (Based on dotnet/runtime guidelines, adapted for this project)

> Source: https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md
> Adapted to project `.editorconfig` conventions. **When in doubt, match existing code in the file.**

### Formatting Fundamentals

| Rule | Convention | Example |
|------|-----------|---------|
| **Indentation** | 2 spaces (project override from runtime's 4) | Enforced by `.editorconfig` |
| **Braces** | Allman style — every brace on its own line | `csharp_new_line_before_open_brace = all` |
| **Namespaces** | File-scoped (WARNING level enforcement) | `namespace Foo.Bar;` not `namespace Foo.Bar { }` |
| **Final newline** | Always insert | `insert_final_newline = true` |
| **Encoding** | UTF-8 with BOM | `charset = utf-8-bom` |
| **Max blank lines** | Never more than one consecutive blank line | — |
| **Trailing spaces** | None | — |

### Naming Rules (NON-NEGOTIABLE)

```
IDENTIFIER             STYLE              PREFIX    EXAMPLE
────────────────────   ────────────────   ───────   ────────────────────────
Public type            PascalCase         —         ContributorService
Public method          PascalCase         —         GetByIdAsync
Public property        PascalCase         —         Name
Parameter              camelCase          —         contributorId
Local variable         camelCase          —         newContributor
Private field          _camelCase         _         _repository
Static private field   s_camelCase        s_        s_defaultTimeout
Thread-static field    t_camelCase        t_        t_currentContext
Constant (any)         PascalCase         —         MaxLength, DefaultPageSize
Local function         PascalCase         —         ValidateInput()
Interface              PascalCase         I         IEmailSender
Type parameter         PascalCase         T         TEntity
Async method           PascalCase         —+Async   GetByIdAsync
```

**CRITICAL — Primary Constructor Parameters:**
- Name as regular parameters: `camelCase`, NO `_` prefix
- For small types (< ~30 lines), use directly: `public class Handler(IRepository<T> repository)`
- For larger types, assign to `_` field: `private readonly IRepository<T> _repository = repository;`
- In this codebase, the common pattern is `_` prefix directly in the constructor parameter for handlers:
  ```csharp
  // This project's convention for handlers (seen throughout codebase):
  public class CreateContributorHandler(IRepository<Contributor> _repository)
    : ICommandHandler<CreateContributorCommand, Result<ContributorId>>
  ```

### Visibility & Modifiers

```csharp
// ALWAYS specify visibility explicitly — never rely on defaults
private string _foo;         // YES
string _foo;                 // NO (implicit private)

// Visibility is FIRST modifier
public abstract void Run();  // YES
abstract public void Run();  // NO

// Modifier order (enforced by .editorconfig):
// public, private, protected, internal, static, extern, new,
// virtual, abstract, sealed, override, readonly, unsafe, volatile, async

// Make private/internal types sealed or static unless derivation is needed
internal sealed class Helper { }
private static class Constants { }
```

### `var` Usage

```csharp
// This project permits var broadly (.editorconfig: csharp_style_var_* = true:silent)
// but PREFER var only when the type is obvious from the right side:

var stream = new FileStream(...);          // YES — type obvious from 'new'
var name = ContributorName.From("test");   // YES — type obvious from method name
var count = items.Count();                 // OK — int is obvious
var result = await _repository.GetByIdAsync(id);  // OK — common pattern

// AVOID var when type is genuinely unclear:
var x = GetData();                         // AVOID — what type is x?
```

### `this.` Usage

```csharp
// AVOID this. — the .editorconfig enforces this:
// dotnet_style_qualification_for_field = false
// dotnet_style_qualification_for_property = false
// dotnet_style_qualification_for_method = false

_repository.GetByIdAsync(id);    // YES
this._repository.GetByIdAsync(id);  // NO
```

### Language Keywords vs BCL Types

```csharp
// ALWAYS use language keywords, not BCL types:
int count = 0;           // YES
Int32 count = 0;         // NO

string name = "test";    // YES
String name = "test";    // NO

int.Parse("42");         // YES
Int32.Parse("42");       // NO
```

### Braces & Single-Statement Blocks

```csharp
// Braces are always accepted. The project prefers them (csharp_prefer_braces = true:silent).
// Braces MAY be omitted ONLY if ALL blocks in a compound statement are single-line:

// YES — braceless single-line (all blocks single-line)
if (entity == null) return Result.NotFound();

// YES — braces always fine
if (entity == null)
{
  return Result.NotFound();
}

// YES — multi-block, all single-line, no braces
if (Name == newName) return this;
else return Update(newName);

// NO — mixed: if one block has braces, ALL must
if (entity == null)
  return Result.NotFound();     // NO — next block has braces
else
{
  entity.UpdateName(newName);
  await _repository.UpdateAsync(entity);
}

// NO — NEVER single-line form
if (source == null) throw new ArgumentNullException("source");  // NO — put on next line
```

### Expression-Bodied Members

```csharp
// Properties and indexers — expression body PREFERRED:
public int Count => _items.Count;
public DbSet<Contributor> Contributors => Set<Contributor>();

// Methods — expression body ONLY for trivial single-expression:
public override string ToString() => $"{CountryCode} {Number}";

// Constructors — block body PREFERRED:
public Delete(IMediator mediator) => _mediator = mediator;  // OK (single assignment)

// Complex methods — ALWAYS block body:
public async ValueTask<Result> Handle(...)  // YES — block body
{
  var entity = await _repository.GetByIdAsync(id);
  if (entity == null) return Result.NotFound();
  // ...
}
```

### Null Handling

```csharp
// Prefer null-conditional and null-coalescing:
var phone = entity.PhoneNumber ?? PhoneNumber.Unknown;      // null-coalescing
handler?.Invoke(this, e);                                    // null-conditional
var name = contributor?.Name ?? "Unknown";                   // chained

// Prefer is null / is not null over == null / != null:
if (entity is null) return Result.NotFound();
if (entity is not null) Process(entity);

// Use nameof() instead of string literals:
throw new ArgumentNullException(nameof(items));              // YES
throw new ArgumentNullException("items");                    // NO

// Guard clauses (via Ardalis.GuardClauses):
Guard.Against.Null(connectionString);
Guard.Against.NegativeOrZero(id);
```

### Pattern Matching

```csharp
// Prefer pattern matching over cast checks:
if (context is not AppDbContext appDbContext) return;         // YES
if (!(context is AppDbContext)) return;                      // NO

// Use when type-checking:
if (result is { IsSuccess: true, Value: var value })
{
  Process(value);
}
```

### `using` Statements & Imports

```csharp
// File-level imports at the top, OUTSIDE namespace, System.* first:
using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Clean.Architecture.Core;

namespace Clean.Architecture.Infrastructure.Data;

// Global usings in GlobalUsings.cs per project (already configured):
// global using Ardalis.Result;
// global using Mediator;

// Prefer simple using statement:
using var scope = app.Services.CreateScope();    // YES
using (var scope = app.Services.CreateScope())   // AVOID (unless nesting needed)
{
}
```

### Fields & Properties

```csharp
// Fields at the TOP of type declarations, before constructors and methods:
public class MyService
{
  // Constants first
  private const int MaxRetries = 3;

  // Static fields
  private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(30);

  // Instance fields
  private readonly IRepository<Contributor> _repository;
  private readonly ILogger<MyService> _logger;

  // Then: constructors, public properties, public methods, private methods
}

// Use readonly wherever possible:
private readonly IMediator _mediator;            // YES — assigned once

// Prefer auto-properties over manual field + property:
public ContributorName Name { get; private set; }   // YES
```

### Async/Await Conventions

```csharp
// Suffix async methods with Async:
public async ValueTask<Result> DeleteContributorAsync(...);
// Exception: Handler methods follow Mediator convention (Handle, not HandleAsync)

// Always use CancellationToken parameter:
public async ValueTask<Result<ContributorId>> Handle(
  CreateContributorCommand command,
  CancellationToken cancellationToken)          // ALWAYS accept, ALWAYS forward

// Prefer ValueTask for handler returns (Mediator convention):
public async ValueTask<Result> Handle(...)      // YES — ValueTask
public async Task<Result> Handle(...)           // NO — use ValueTask

// ConfigureAwait: NOT needed in ASP.NET Core (no SynchronizationContext)
```

### Record & Class Conventions

```csharp
// Commands, Queries, Events, DTOs — use records (immutable by default):
public record CreateContributorCommand(ContributorName Name) : ICommand<Result<ContributorId>>;
public record ContributorDto(ContributorId Id, ContributorName Name, PhoneNumber Phone);
public sealed class ContributorDeletedEvent(ContributorId id) : DomainEventBase;

// Handlers, Services, Endpoints — use classes:
public class CreateContributorHandler(...) : ICommandHandler<...> { }
public class DeleteContributorService(...) : IDeleteContributorService { }

// API Requests — use classes (mutable, bound from HTTP):
public class CreateContributorRequest { public string Name { get; set; } = string.Empty; }

// API Responses — use classes with primary constructors:
public class CreateContributorResponse(int id, string name) { ... }

// API Records (read-only projections) — use records:
public record ContributorRecord(int Id, string Name, string Phone);
```

### Non-ASCII & Special Characters

```csharp
// Use Unicode escape sequences for non-ASCII characters:
string bullet = "\u2022";       // YES
string bullet = "•";            // NO — may be garbled by tools
```

### CODING STYLE QUICK REFERENCE

```
ALWAYS DO                           NEVER DO
──────────────────────────────      ──────────────────────────────
2-space indentation                 Tabs or 4-space in this project
Allman braces (own line)            K&R / same-line opening brace
File-scoped namespaces              Block-scoped namespaces
Explicit visibility                 Implicit private/internal
_prefix private fields              m_ or no prefix
PascalCase constants                UPPER_SNAKE_CASE constants
Language keywords (int, string)     BCL types (Int32, String)
nameof(param)                       "param" string literal
readonly when possible              Mutable fields without reason
sealed on private/internal types    Open types without derivation need
Expression-body for trivial props   Expression-body for complex methods
Result<T> for expected failures     Exceptions for flow control
Guard clauses for preconditions     Deep nesting with if/else
```

---

## PERFORMANCE GUIDELINES (Based on dotnet/runtime performance guidance)

> Sources:
> - https://github.com/dotnet/runtime/blob/main/docs/project/performance-guidelines.md
> - https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/performance-guidelines.md
>
> Applied to Clean Architecture with EF Core, Mediator, and FastEndpoints.

### Design-Phase Principles

Before writing code, evaluate performance implications of your design:

| Principle | Meaning |
|-----------|---------|
| **Wide scenario coverage** | A change that benefits one query but regresses ten others will be rejected |
| **Pay for play** | Whoever pays the cost must also get the benefit. Don't penalize simple paths for complex ones |
| **Justify complexity** | Caches, eager loading, denormalization — all need a compelling measured reason |
| **Prototype first** | If unsure about perf characteristics, write a throwaway prototype and measure before committing to a design |

### Memory & Allocations

```csharp
// ── AVOID unnecessary allocations ──────────────────────────────────────

// BAD: Lambda captures 'this', allocates closure + delegate every call
items.Where(x => x.Id == _targetId).ToList();

// BETTER: Use a local variable to avoid closure over 'this'
var targetId = _targetId;
items.Where(x => x.Id == targetId).ToList();

// ── AVOID allocating when returning empty collections ──────────────────

// BAD: Allocates a new list every time
return new List<ContributorDto>();

// GOOD: Use singleton empty collections
return Array.Empty<ContributorDto>();
// or
return [];  // Collection expression — compiler optimizes to empty

// ── PREFER struct / readonly struct for small, short-lived types ───────

// Vogen value objects are already structs — this is correct:
[ValueObject<int>]
public readonly partial struct ContributorId { }  // Stack-allocated, no GC pressure

// ── AVOID excessive string allocations ─────────────────────────────────

// BAD: Multiple intermediate string allocations
var msg = "Contributor " + id.ToString() + " was " + action + " by " + user;

// GOOD: String interpolation (compiler optimizes for simple cases)
var msg = $"Contributor {id} was {action} by {user}";

// BETTER for hot paths: Use string.Create or StringBuilder
// (Not needed in typical CRUD handlers — only for truly hot code)

// ── AVOID boxing value types ───────────────────────────────────────────

// BAD: Boxing int to object via interface
object id = contributorId.Value;  // boxes the int

// GOOD: Stay generic or use concrete types
int id = contributorId.Value;     // no boxing
```

### Async/Await Performance

```csharp
// ── USE ValueTask for handlers (already required by Mediator) ──────────

// Mediator handlers return ValueTask — this is CORRECT and more efficient
// than Task when the result is often synchronous (cache hits, validation failures).
public async ValueTask<Result<ContributorId>> Handle(
  CreateContributorCommand command, CancellationToken ct) { }

// ── AVOID async when the method is trivially synchronous ───────────────

// BAD: Unnecessary async state machine allocation
public async ValueTask<Result> Handle(DeleteContributorCommand request, CancellationToken ct)
{
  return await _service.DeleteContributor(request.ContributorId);
}

// GOOD: Pass-through without async — avoids state machine allocation
public ValueTask<Result> Handle(DeleteContributorCommand request, CancellationToken ct) =>
  _service.DeleteContributor(request.ContributorId);

// ── ALWAYS forward CancellationToken ───────────────────────────────────

// BAD: Ignoring cancellation — wastes resources on cancelled requests
var entity = await _repository.GetByIdAsync(id);

// GOOD: Forward the token so work stops when the client disconnects
var entity = await _repository.GetByIdAsync(id, cancellationToken);

// ── AVOID capturing variables in async lambdas on hot paths ────────────

// Async lambdas that capture local variables allocate a closure AND a state machine.
// Fine in setup/config code. Avoid in per-request handler code.

// ── ConfigureAwait(false) ──────────────────────────────────────────────

// NOT needed in ASP.NET Core (no SynchronizationContext).
// Do NOT add ConfigureAwait(false) — it just clutters the code.
```

### EF Core Performance (Infrastructure Layer)

```csharp
// ── USE AsNoTracking() for all read-only queries ───────────────────────

// The List query service already does this correctly:
var items = await _db.Contributors
  .OrderBy(c => c.Id)
  .Skip((page - 1) * perPage)
  .Take(perPage)
  .Select(c => new ContributorDto(c.Id, c.Name, c.PhoneNumber ?? PhoneNumber.Unknown))
  .AsNoTracking()     // ← No change tracking overhead
  .ToListAsync();

// RULE: Every query that does NOT update entities must use AsNoTracking()
// or be projected via .Select() (which is implicitly no-tracking).

// ── PROJECT with .Select() instead of loading full entities ────────────

// BAD: Loads entire entity graph into memory, then maps
var entities = await _db.Contributors.ToListAsync();
return entities.Select(e => new ContributorDto(e.Id, e.Name, ...)).ToList();

// GOOD: Project in the query — only fetches needed columns from DB
var dtos = await _db.Contributors
  .Select(c => new ContributorDto(c.Id, c.Name, c.PhoneNumber ?? PhoneNumber.Unknown))
  .ToListAsync();

// ── AVOID N+1 queries — use Include() or projections ───────────────────

// BAD: Loads parent, then lazy-loads children per iteration
var project = await _repo.GetByIdAsync(id);
foreach (var item in project.Items)  // N+1 if Items is lazy-loaded
{
  Process(item);
}

// GOOD: Eager load with Specification
public class ProjectWithItemsSpec : Specification<Project>
{
  public ProjectWithItemsSpec(ProjectId id) =>
    Query.Where(p => p.Id == id).Include(p => p.Items);
}

// ── USE raw SQL or FromSqlRaw for complex read queries ─────────────────

// The architecture explicitly supports this for List operations:
// Queries bypass the repository and use IQueryService directly.
// This is WHERE you optimize — select only needed columns.
var items = await _db.Contributors
  .FromSqlRaw("SELECT Id, Name, PhoneNumber_CountryCode, PhoneNumber_Number, PhoneNumber_Extension FROM Contributors")
  .OrderBy(c => c.Id)
  .Skip(offset).Take(limit)
  .ToListAsync();

// ── AVOID loading entities just to delete them ─────────────────────────

// The current pattern loads first, then deletes:
var entity = await _repository.GetByIdAsync(id);
await _repository.DeleteAsync(entity);

// This is ACCEPTABLE when domain events must be raised (they need the entity).
// For bulk deletes without events, consider ExecuteDeleteAsync (EF Core 7+):
// await _db.Contributors.Where(c => c.Id == id).ExecuteDeleteAsync();

// ── PAGINATION is mandatory for list endpoints ─────────────────────────

// NEVER return unbounded result sets. The template enforces this:
// - Default page size: Constants.DEFAULT_PAGE_SIZE
// - Max page size: Constants.MAX_PAGE_SIZE
// - Always .Skip().Take() in query services
```

### Dependency Injection & Lifetime Performance

```csharp
// ── SCOPED for DbContext and Repositories (already configured) ─────────

// DbContext is scoped — one instance per HTTP request.
// EfRepository<T> is scoped — shares the DbContext.
// NEVER register DbContext as Singleton or Transient.

// ── SCOPED for Mediator (configured in MediatorConfig) ─────────────────

// Mediator is registered Scoped. This ensures handlers share
// the same DbContext and participate in the same unit of work.

// ── AVOID resolving services in hot loops ──────────────────────────────

// BAD: Resolves from DI container per iteration
foreach (var id in ids)
{
  var handler = serviceProvider.GetRequiredService<IHandler>();
  await handler.Process(id);
}

// GOOD: Resolve once, reuse
var handler = serviceProvider.GetRequiredService<IHandler>();
foreach (var id in ids)
{
  await handler.Process(id);
}
```

### Caching Considerations

```
BEFORE adding a cache, answer ALL of these:

1. COMPELLING SCENARIO: Is there a measured, specific performance problem?
   → Don't add caches "just in case"

2. PAY FOR PLAY: Does every consumer of this cache benefit?
   → If read-rarely consumers pay memory cost, the cache is at the wrong layer

3. BOUNDED SIZE: Is the cache bounded?
   → Unbounded caches are memory leaks. Always set max size.

4. LIFETIME MATCH: Does cache lifetime match data freshness needs?
   → Long-lived caches for frequently-changing data cause stale reads.

5. INVALIDATION: How is the cache invalidated?
   → "Cache invalidation is one of two hard problems in CS." Have a plan.

WHERE to cache in Clean Architecture:
┌─────────────────────────────────────────────────────────┐
│  Web (endpoint)     →  Response caching, HTTP caching   │
│  UseCases (handler) →  Application-level caching        │
│  Infrastructure     →  Query result caching             │
│  Core               →  NEVER cache here                 │
└─────────────────────────────────────────────────────────┘
```

### Logging Performance

```csharp
// ── USE structured logging with templates, NOT string interpolation ────

// BAD: String is allocated even if log level is disabled
_logger.LogInformation($"Deleting contributor {contributorId}");

// GOOD: Template — allocation only happens if log level is enabled
_logger.LogInformation("Deleting contributor {ContributorId}", contributorId);

// ── USE LoggerMessage.Define for ultra-hot paths ───────────────────────

// For code that logs on every request, pre-compile the log message:
private static readonly Action<ILogger, ContributorId, Exception?> _logDeleting =
  LoggerMessage.Define<ContributorId>(
    LogLevel.Information,
    new EventId(1, nameof(DeleteContributor)),
    "Deleting contributor {ContributorId}");

// Usage:
_logDeleting(_logger, contributorId, null);

// NOTE: For typical CRUD handlers, the template approach is sufficient.
// LoggerMessage.Define is only needed in middleware, interceptors, or high-RPS paths.

// ── GUARD expensive log message construction ───────────────────────────

// BAD: Serializes object even when debug logging is off
_logger.LogDebug("Entity state: {State}", JsonSerializer.Serialize(entity));

// GOOD: Check level first
if (_logger.IsEnabled(LogLevel.Debug))
{
  _logger.LogDebug("Entity state: {State}", JsonSerializer.Serialize(entity));
}
```

### Mediator & Pipeline Performance

```csharp
// ── SOURCE GENERATOR (already configured) ──────────────────────────────

// This project uses Mediator.SourceGenerator, NOT MediatR (reflection-based).
// The source generator produces compile-time dispatch code — no reflection,
// no dictionary lookups, no runtime type resolution. This is FAST.

// ── PIPELINE BEHAVIORS are per-request overhead ────────────────────────

// Each behavior in the pipeline wraps every command/query.
// Currently only LoggingBehavior is registered — this is lightweight.
//
// If adding behaviors (validation, caching, auth), measure the impact.
// Order matters — put cheap checks (validation) before expensive ones (caching).

options.PipelineBehaviors =
[
  typeof(ValidationBehavior<,>),  // Cheap — fail fast
  typeof(LoggingBehavior<,>),     // Cheap — structured log
  typeof(CachingBehavior<,>),     // Expensive — only if needed
];
```

### PERFORMANCE QUICK REFERENCE

```
ALWAYS DO                                NEVER DO
───────────────────────────────────      ───────────────────────────────────
AsNoTracking() on read queries           Load full entities for read-only display
.Select() projection in queries          .ToList() then .Select() in memory
Forward CancellationToken everywhere     Ignore cancellation tokens
Paginate all list endpoints              Return unbounded result sets
Structured log templates                 String interpolation in log calls
ValueTask for handler returns            Task when ValueTask is expected
Skip async for trivial pass-throughs     Async state machine for one-liner awaits
Bounded caches with invalidation         Unbounded or "just in case" caches
Resolve DI services once, reuse          Resolve services inside loops
Include() or projection for relations    Let N+1 queries happen silently
Source-generated Mediator (compile-time) Reflection-based MediatR at scale
```

---

## GC & MEMORY MANAGEMENT (Avoiding Stop-the-World and Memory Leaks)

> Sources:
> - https://github.com/dotnet/runtime/blob/main/docs/project/garbage-collector-guidelines.md
> - https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals
> - https://learn.microsoft.com/dotnet/standard/garbage-collection/large-object-heap

### How the GC Works (What You Must Know)

```
┌─────────────────────────────────────────────────────────────────────┐
│                        MANAGED HEAP                                 │
│                                                                     │
│  Gen 0 (Ephemeral)     Gen 1 (Buffer)     Gen 2 (Long-lived)       │
│  ┌──────────────┐     ┌──────────────┐    ┌──────────────────┐     │
│  │ New objects   │────▸│ Survived G0  │───▸│ Survived G1      │     │
│  │ Cheap to GC   │     │ Cheap to GC  │    │ EXPENSIVE to GC  │     │
│  │ ~microseconds │     │ ~milliseconds│    │ ~10s of ms       │     │
│  └──────────────┘     └──────────────┘    │ "STOP THE WORLD" │     │
│                                            └──────────────────┘     │
│                                                                     │
│  Large Object Heap (LOH) — objects ≥ 85,000 bytes                   │
│  ┌──────────────────────────────────────────────────────┐           │
│  │ Collected ONLY during Gen2 (full GC)                 │           │
│  │ NOT compacted by default → fragmentation risk        │           │
│  │ Every LOH allocation can trigger a full GC           │           │
│  └──────────────────────────────────────────────────────┘           │
└─────────────────────────────────────────────────────────────────────┘

CRITICAL FACTS:
• Gen0/Gen1 collections are FAST and CHEAP — don't worry about them.
• Gen2 (full GC) is EXPENSIVE — pauses ALL managed threads ("stop the world").
• Objects that survive Gen0→Gen1→Gen2 promotions live longer in memory.
• LOH objects are ALWAYS Gen2 — creating large temporary objects is costly.
• The more you allocate, the more frequently GC runs.
• The GOAL: keep objects short-lived (die in Gen0) or truly permanent (Gen2).
  Avoid the "mid-life crisis" where objects survive to Gen2 unnecessarily.
```

### ASP.NET Core GC Configuration

```
ASP.NET Core uses Server GC by default (one heap per logical CPU core).

Server GC:
  ✓ Higher throughput (parallel collection across cores)
  ✓ Better for web servers with many concurrent requests
  ✗ Higher memory usage (larger segments per core)
  ✗ Gen2 pauses can be longer (more memory to scan)

For most Clean Architecture web APIs, Server GC is correct.
DO NOT change to Workstation GC unless profiling shows a specific benefit.

// To verify (in .csproj, already default for web apps):
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
</PropertyGroup>
```

### Rule 1: Minimize Allocations (Reduce GC Pressure)

```csharp
// ── Every allocation brings the next GC closer ────────────────────────
// In a high-RPS web API, per-request allocations multiply fast.
// 1000 RPS × 10 allocations/request = 10,000 allocations/second.

// ── AVOID allocating in hot loops ─────────────────────────────────────

// BAD: New string allocated per iteration
foreach (var contributor in contributors)
{
  var label = $"Contributor: {contributor.Name}";  // allocation per item
  Process(label);
}

// BETTER: Use a reusable StringBuilder for building strings in loops
var sb = new StringBuilder();
foreach (var contributor in contributors)
{
  sb.Clear();
  sb.Append("Contributor: ").Append(contributor.Name);
  Process(sb.ToString());  // still allocates, but StringBuilder itself is reused
}

// ── USE ArrayPool for temporary arrays ────────────────────────────────

// BAD: Allocates array on heap; if ≥ 85KB, goes to LOH
var buffer = new byte[100_000];  // 100KB → LOH allocation!

// GOOD: Rent from pool → no GC pressure, no LOH
var buffer = ArrayPool<byte>.Shared.Rent(100_000);
try
{
  // Use buffer...
}
finally
{
  ArrayPool<byte>.Shared.Return(buffer);
}

// ── USE Span<T> / Memory<T> for slicing without allocation ────────────

// BAD: Substring allocates a new string
string name = fullName.Substring(0, 10);

// GOOD: Span slices without allocation (stack only)
ReadOnlySpan<char> name = fullName.AsSpan(0, 10);

// ── AVOID LINQ in ultra-hot paths ─────────────────────────────────────

// LINQ allocates iterators, delegates, and intermediate collections.
// Fine for CRUD handlers (clarity > micro-optimization).
// AVOID in per-request middleware, interceptors, or tight loops.

// Allocates: iterator + delegate + List
var ids = contributors.Where(c => c.Status == active).Select(c => c.Id).ToList();

// Zero-allocation alternative for hot paths:
var ids = new List<ContributorId>(contributors.Count);
foreach (var c in contributors)
{
  if (c.Status == active) ids.Add(c.Id);
}
```

### Rule 2: Avoid LOH Allocations (≥ 85,000 bytes)

```csharp
// LOH objects are ONLY collected during Gen2 (full GC / stop-the-world).
// Every LOH allocation risks triggering a full GC.

// ── KNOW THE THRESHOLD ────────────────────────────────────────────────

// Object ≥ 85,000 bytes → LOH. For arrays:
// byte[85_000]        → LOH (85,000 bytes)
// int[21_250]         → LOH (21,250 × 4 = 85,000 bytes)
// object[10_625]      → LOH (10,625 × 8 = 85,000 bytes on 64-bit)
// string of ~42,500 chars → LOH (each char = 2 bytes + object overhead)

// ── USE ArrayPool for large buffers ───────────────────────────────────

// BAD: 1MB buffer on LOH every time this method is called
byte[] buffer = new byte[1_048_576];

// GOOD: Pool manages LOH buffers — rented, returned, reused
byte[] buffer = ArrayPool<byte>.Shared.Rent(1_048_576);
try { /* use buffer */ }
finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }

// ── BEWARE: ToList() / ToArray() on large query results ───────────────

// If your query returns 10,000+ entities, the resulting List<T> backing
// array could exceed 85KB and land on the LOH.
// THIS IS WHY PAGINATION IS MANDATORY — not just for the client, but
// to keep per-request allocations below the LOH threshold.

// BAD: Potentially unbounded LOH allocation
var all = await _db.Contributors.ToListAsync();

// GOOD: Bounded, predictable allocation size
var page = await _db.Contributors
  .Skip(offset).Take(pageSize)  // pageSize ≤ MAX_PAGE_SIZE (e.g. 100)
  .ToListAsync();

// ── AVOID large string concatenation ──────────────────────────────────

// BAD: Building large response body as string (LOH if > ~42K chars)
var json = JsonSerializer.Serialize(largeObjectGraph);

// GOOD: Stream directly to response (no intermediate string)
// FastEndpoints and ASP.NET Core handle this automatically when you
// return typed responses — the serializer writes to the response stream.
```

### Rule 3: Prevent Memory Leaks

```csharp
// In .NET, "memory leak" = objects kept alive unintentionally by references.
// The GC cannot collect objects that are still reachable from a root.

// ══════════════════════════════════════════════════════════════════════
// LEAK #1: EVENT HANDLER LEAK (most common in long-lived services)
// ══════════════════════════════════════════════════════════════════════

// BAD: Subscribing without unsubscribing — publisher holds reference to subscriber
public class NotificationService
{
  public NotificationService(IEventBus bus)
  {
    bus.OnMessage += HandleMessage;  // 'this' is now rooted by bus
    // If bus outlives this service, this service can NEVER be collected
  }
}

// GOOD: Unsubscribe in Dispose
public class NotificationService : IDisposable
{
  private readonly IEventBus _bus;

  public NotificationService(IEventBus bus)
  {
    _bus = bus;
    _bus.OnMessage += HandleMessage;
  }

  public void Dispose()
  {
    _bus.OnMessage -= HandleMessage;  // Release the reference
  }
}

// NOTE: In Clean Architecture, domain events via Mediator don't have this
// problem — handlers are resolved per-scope and disposed automatically.

// ══════════════════════════════════════════════════════════════════════
// LEAK #2: STATIC REFERENCES (live for entire process lifetime)
// ══════════════════════════════════════════════════════════════════════

// BAD: Static collection grows forever
private static readonly List<ContributorDto> _cache = new();

public void AddToCache(ContributorDto dto)
{
  _cache.Add(dto);  // Never removed → grows until OOM
}

// GOOD: Use bounded cache with eviction policy
// Use IMemoryCache (built-in) with size limits and expiration:
services.AddMemoryCache(options =>
{
  options.SizeLimit = 1000;  // Max 1000 entries
});

// ══════════════════════════════════════════════════════════════════════
// LEAK #3: CLOSURE CAPTURES keeping objects alive
// ══════════════════════════════════════════════════════════════════════

// BAD: Lambda captures 'largeData' — kept alive until Task completes
byte[] largeData = LoadLargeFile();
_ = Task.Run(() =>
{
  // largeData is captured by closure — cannot be GC'd until Task finishes
  Process(largeData);
});

// GOOD: Null out after use, or scope tightly
byte[] largeData = LoadLargeFile();
var result = Process(largeData);
largeData = null;  // Eligible for GC now (if no other references)

// ══════════════════════════════════════════════════════════════════════
// LEAK #4: TIMER LEAKS
// ══════════════════════════════════════════════════════════════════════

// BAD: Timer root prevents GC of the enclosing object
public class PollingService
{
  private readonly Timer _timer;

  public PollingService()
  {
    _timer = new Timer(_ => Poll(), null, 0, 5000);
    // Timer pins this object via callback — GC cannot collect it
  }
}

// GOOD: Dispose the timer
public class PollingService : IDisposable
{
  private readonly Timer _timer;

  public PollingService()
  {
    _timer = new Timer(_ => Poll(), null, 0, 5000);
  }

  public void Dispose() => _timer?.Dispose();
}

// ══════════════════════════════════════════════════════════════════════
// LEAK #5: CancellationTokenSource not disposed
// ══════════════════════════════════════════════════════════════════════

// BAD: CTS allocates a Timer internally when using CancelAfter
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(30));
// If not disposed, the internal Timer leaks

// GOOD: Always dispose CTS
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await _mediator.Send(command, cts.Token);
```

### Rule 4: IDisposable / IAsyncDisposable Patterns

```csharp
// ── RULE: If you own an IDisposable, you MUST dispose it ──────────────

// In Clean Architecture, DI handles most disposal automatically:
// - DbContext → Scoped → disposed at end of request
// - EfRepository → Scoped → disposed at end of request
// - HttpClient → via IHttpClientFactory (pooled)

// YOU must handle disposal when creating disposable objects manually:

// ── USE 'using' declarations (C# 8+) ─────────────────────────────────

// GOOD: Disposed at end of enclosing scope
using var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
await SeedData.InitializeAsync(context);
// scope disposed here ← automatic

// ── IMPLEMENT IAsyncDisposable for async resources ────────────────────

public class FileExportService : IAsyncDisposable
{
  private readonly StreamWriter _writer;

  public FileExportService(string path)
  {
    _writer = new StreamWriter(path);
  }

  public async ValueTask DisposeAsync()
  {
    await _writer.DisposeAsync();
  }
}

// Usage:
await using var exporter = new FileExportService("/tmp/export.csv");

// ── NEVER implement finalizers unless wrapping unmanaged resources ─────

// Finalizers (~ClassName) cause objects to survive an EXTRA GC cycle:
// 1st GC: Object found unreachable → moved to finalization queue (survives!)
// 2nd GC: Finalizer runs → object FINALLY collected
// This means finalized objects are promoted to Gen1 or Gen2 unnecessarily.

// BAD: Finalizer on a managed-only class
public class MyService
{
  ~MyService() { /* cleanup */ }  // NO! Causes GC promotion penalty
}

// GOOD: Use IDisposable, not finalizers
public class MyService : IDisposable
{
  public void Dispose() { /* cleanup */ }
}

// Only use finalizer + Dispose pattern together when directly wrapping
// unmanaged resources (file handles, native memory, COM objects).
// In Clean Architecture, you almost NEVER need finalizers.
```

### Rule 5: EF Core Memory Pitfalls

```csharp
// ── CHANGE TRACKER BLOAT ──────────────────────────────────────────────

// DbContext tracks every entity it loads. In long operations:
// - 10,000 tracked entities = significant memory + slow SaveChanges
// - Change detection scans ALL tracked entities on every SaveChanges

// GOOD: Use AsNoTracking for read-only queries (already covered in perf)
// GOOD: Use separate DbContext scope for bulk reads

// For bulk operations, detach or use a fresh context:
using var scope = _serviceScopeFactory.CreateScope();
var freshContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
// freshContext has empty change tracker

// ── DbContext LIFETIME — SCOPED, NEVER SINGLETON ──────────────────────

// DbContext as Singleton = memory leak. It will track every entity
// for the entire process lifetime. Change tracker grows until OOM.
// This is ALREADY correctly configured as Scoped in this template.

// ── QUERY RESULT MATERIALIZATION ──────────────────────────────────────

// BAD: Materializes entire table into memory, THEN filters
var contributors = await _db.Contributors.ToListAsync();
var active = contributors.Where(c => c.Status == active);

// GOOD: Filter in SQL, materialize only results
var active = await _db.Contributors
  .Where(c => c.Status == active)
  .ToListAsync();

// CRITICAL: ToListAsync() is the materialization point.
// Everything BEFORE it runs as SQL. Everything AFTER runs in memory.
// Put ALL filtering, sorting, and pagination BEFORE ToListAsync().
```

### Rule 6: Avoid Mid-Life Crisis Objects

```csharp
// "Mid-life crisis" = objects that live just long enough to be promoted
// from Gen0 to Gen1/Gen2, but then die. These are the WORST for GC
// because they cause Gen2 collections for temporary data.

// ── COMMON CAUSE: Async state machines holding large references ───────

// When you await, the compiler creates a state machine struct.
// All local variables that are used AFTER the await are captured
// as fields on this struct. If the await takes time, the struct
// (and everything it captures) survives Gen0 → Gen1 → maybe Gen2.

// BAD: Large array captured across await boundary
public async ValueTask<Result> ProcessAsync(CancellationToken ct)
{
  var largeBuffer = new byte[50_000];  // Captured in state machine
  FillBuffer(largeBuffer);

  await _repository.SaveAsync(ct);     // If slow, largeBuffer promoted

  return Result.Success();
}

// GOOD: Release reference before await
public async ValueTask<Result> ProcessAsync(CancellationToken ct)
{
  byte[] largeBuffer = ArrayPool<byte>.Shared.Rent(50_000);
  try
  {
    FillBuffer(largeBuffer);
    var processedData = ExtractResult(largeBuffer);
  }
  finally
  {
    ArrayPool<byte>.Shared.Return(largeBuffer);  // Returned BEFORE await
  }

  await _repository.SaveAsync(ct);  // State machine no longer holds buffer
  return Result.Success();
}

// ── COMMON CAUSE: Per-request objects registered as Scoped ────────────

// Scoped services live for the entire HTTP request. If a request takes
// 5+ seconds (slow DB, external API), all scoped objects survive to Gen2.
// This is NORMAL and expected — don't fight it.
// Just ensure scoped objects are SMALL (no large buffers as fields).
```

### GC MEMORY MANAGEMENT QUICK REFERENCE

```
PRODUCTION KILLERS (will cause stop-the-world pauses)
─────────────────────────────────────────────────────
• Temporary LOH allocations (≥ 85KB arrays created and discarded)
• Unbounded ToListAsync() loading entire tables
• Static collections that grow without limit
• Singleton DbContext (change tracker grows forever)
• Large object graphs captured across await boundaries
• Finalizers on managed-only classes (doubles GC cost)

MEMORY LEAK PATTERNS (process memory grows until OOM/restart)
─────────────────────────────────────────────────────
• Event += without -= in long-lived services
• Static List/Dictionary without eviction
• Timer not disposed (pins callback target)
• CancellationTokenSource not disposed (leaks internal Timer)
• Closures capturing large objects in background Tasks
• Undisposed DbContext in manual scopes

SAFE PATTERNS (follow these and you won't have GC problems)
─────────────────────────────────────────────────────
• Keep per-request objects small and short-lived (die in Gen0)
• Paginate everything (max ~100 items per query)
• ArrayPool for any buffer > 1KB
• using/await using for all IDisposable
• Scoped DbContext (disposed per request — already configured)
• Vogen value objects as structs (stack-allocated, zero GC)
• Bounded IMemoryCache with size limits and expiration
• DI manages service lifetimes — trust it, don't fight it
```

### When to Profile (Not Before)

```
DO NOT micro-optimize GC without evidence. Profile FIRST:

TOOLS:
  dotnet-counters  → Live GC metrics (gen0/1/2 counts, LOH size, alloc rate)
  dotnet-dump       → Heap snapshot analysis
  dotnet-trace      → ETW trace for GC events
  PerfView          → Deep GC analysis with call stacks
  Visual Studio     → Memory profiler (allocation tracking)

METRICS TO WATCH:
  • Gen2 collection count — should be LOW relative to Gen0
  • LOH size              — should be STABLE, not growing
  • GC pause time         — should be < 100ms for web APIs
  • Allocation rate       — MB/sec; lower = fewer GC cycles

WHEN TO ACT:
  • Gen2 collections > 10% of total GC collections
  • LOH growing steadily over hours without plateau
  • p99 latency spikes correlating with GC pauses
  • Process memory growing without stabilizing (leak)

COMMAND EXAMPLE:
  dotnet-counters monitor -p <PID> --counters \
    System.Runtime[gen-0-gc-count,gen-1-gc-count,gen-2-gc-count,\
    gc-heap-size,alloc-rate,time-in-gc]
```

---

## NAMING CONVENTIONS

| Concept | Convention | Example |
|---------|-----------|---------|
| Aggregate folder | `{Name}Aggregate` | `ContributorAggregate/` |
| Entity class | PascalCase noun | `Contributor` |
| Value Object (Vogen) | PascalCase noun | `ContributorName` |
| Strongly-typed ID | `{Entity}Id` | `ContributorId` |
| Smart Enum | `{Entity}{Property}` | `ContributorStatus` |
| Domain Event | `{Entity}{Action}Event` | `ContributorNameUpdatedEvent` |
| Event Handler | `{EventName}Handler` or `{EventName}{Purpose}Handler` | `ContributorDeletedHandler` |
| Specification | `{Entity}By{Criteria}Spec` | `ContributorByIdSpec` |
| Command | `{Action}{Entity}Command` | `CreateContributorCommand` |
| Command Handler | `{Action}{Entity}Handler` | `CreateContributorHandler` |
| Query | `{Action}{Entity}Query` | `GetContributorQuery` |
| Query Handler | `{Action}{Entity}Handler` | `GetContributorHandler` |
| Query Service Interface | `IList{Entity}QueryService` | `IListContributorsQueryService` |
| DTO | `{Entity}Dto` | `ContributorDto` |
| Endpoint | Verb: `Create`, `GetById`, `List`, `Update`, `Delete` | `Create.cs` |
| API Request | `{Action}{Entity}Request` | `CreateContributorRequest` |
| API Response | `{Action}{Entity}Response` | `CreateContributorResponse` |
| API Validator | `{Action}{Entity}Validator` | `CreateContributorValidator` |
| API Record | `{Entity}Record` | `ContributorRecord` |
| EF Config | `{Entity}Configuration` | `ContributorConfiguration` |
| DI Extension | `{Layer}ServiceExtensions` | `InfrastructureServiceExtensions` |

---

## TECHNOLOGY REFERENCE

| Library | Purpose | Used In |
|---------|---------|---------|
| `Ardalis.SharedKernel` | Base classes: `EntityBase`, `ValueObject`, `DomainEventBase`, `IAggregateRoot` | Core |
| `Ardalis.GuardClauses` | Input validation: `Guard.Against.Null()`, `.NegativeOrZero()` | Core |
| `Ardalis.Specification` | Query encapsulation: `Specification<T>`, `Query.Where().Include()` | Core |
| `Ardalis.SmartEnum` | Rich enums: `SmartEnum<T>` | Core |
| `Ardalis.Result` | Error handling: `Result<T>`, `Result.Success()`, `.NotFound()`, `.Invalid()` | Core, UseCases |
| `Vogen` | Value objects: `[ValueObject<T>]` | Core |
| `Mediator` (Source Generator) | CQRS: `ICommand<T>`, `IQuery<T>`, `ICommandHandler<,>`, `IQueryHandler<,>`, `INotificationHandler<>` | All |
| `FastEndpoints` | API: `Endpoint<TReq, TResp>`, `Validator<T>`, `Mapper<,,>` | Web |
| `FluentValidation` | Request validation (via FastEndpoints integration) | Web |
| `EF Core` | ORM: `DbContext`, `IEntityTypeConfiguration<T>`, migrations | Infrastructure |
| `Ardalis.Specification.EntityFrameworkCore` | Repository: `RepositoryBase<T>` | Infrastructure |
| `MailKit` | Email sending via SMTP | Infrastructure |
| `Serilog` | Structured logging | Web |
| `Scalar` | API documentation UI | Web |
