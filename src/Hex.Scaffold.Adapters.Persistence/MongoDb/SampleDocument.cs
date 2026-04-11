using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Hex.Scaffold.Adapters.Persistence.MongoDb;

public sealed class SampleDocument
{
  [BsonId]
  [BsonRepresentation(BsonType.ObjectId)]
  public string? Id { get; set; }

  public int SampleId { get; set; }
  public string Name { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public string? Description { get; set; }
  public DateTime LastUpdated { get; set; }
}
