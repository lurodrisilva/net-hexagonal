namespace Hex.Scaffold.Adapters.Persistence.MongoDb;

public sealed class MongoDbOptions
{
  public string ConnectionString { get; set; } = string.Empty;
  public string DatabaseName { get; set; } = "hex-scaffold";
}
