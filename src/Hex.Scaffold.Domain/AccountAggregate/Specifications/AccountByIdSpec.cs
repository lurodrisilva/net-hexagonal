namespace Hex.Scaffold.Domain.AccountAggregate.Specifications;

public sealed class AccountByIdSpec : Specification<Account>
{
  public AccountByIdSpec(AccountId id)
  {
    Query.Where(a => a.Id == id);
  }
}
