namespace Hex.Scaffold.Application.Samples.Get;

public record GetExternalSampleInfoQuery(string Endpoint) : IQuery<Result<string>>;
