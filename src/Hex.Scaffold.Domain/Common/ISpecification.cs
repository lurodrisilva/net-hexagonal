using System.Linq.Expressions;

namespace Hex.Scaffold.Domain.Common;

public interface ISpecification<T>
{
  Expression<Func<T, bool>>? WhereExpression { get; }
}
