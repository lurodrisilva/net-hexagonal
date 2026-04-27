namespace Hex.Scaffold.Application.Accounts.List;

// `limit` defaults to 10 and is capped at 100, matching Stripe.
// Mutually exclusive: `starting_after` (forward) vs `ending_before` (back).
public sealed record ListAccountsQuery(
  int Limit,
  string? StartingAfter,
  string? EndingBefore)
  : IQuery<Result<AccountListResult>>;
