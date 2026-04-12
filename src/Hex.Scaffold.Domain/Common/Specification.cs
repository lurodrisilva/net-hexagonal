using System.Linq.Expressions;

namespace Hex.Scaffold.Domain.Common;

public abstract class Specification<T> : ISpecification<T>
{
  public Expression<Func<T, bool>>? WhereExpression { get; private set; }

  protected SpecificationBuilder<T> Query => new(this);

  internal void SetWhereExpression(Expression<Func<T, bool>> expression) =>
    WhereExpression = expression;
}

public sealed class SpecificationBuilder<T>
{
  private readonly Specification<T> _specification;

  internal SpecificationBuilder(Specification<T> specification) =>
    _specification = specification;

  public SpecificationBuilder<T> Where(Expression<Func<T, bool>> expression)
  {
    _specification.SetWhereExpression(expression);
    return this;
  }
}
