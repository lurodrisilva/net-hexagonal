namespace Hex.Scaffold.Application;

public record PagedResult<T>(
  List<T> Items,
  int Page,
  int PerPage,
  int TotalCount,
  int TotalPages);
