namespace Hex.Scaffold.Domain.Common;

public abstract class EntityBase<TSelf, TId> : HasDomainEventsBase
  where TSelf : EntityBase<TSelf, TId>
{
  public TId Id { get; set; } = default!;
}
