using Hex.Scaffold.Domain.Ports.Outbound;
using MongoDB.Driver;

namespace Hex.Scaffold.Adapters.Persistence.MongoDb;

public sealed class SampleReadModelRepository(
  IMongoClient _mongoClient,
  IOptions<MongoDbOptions> _options,
  ILogger<SampleReadModelRepository> _logger) : ISampleReadModelRepository
{
  private IMongoCollection<SampleDocument> GetCollection()
  {
    var db = _mongoClient.GetDatabase(_options.Value.DatabaseName);
    return db.GetCollection<SampleDocument>("samples");
  }

  public async Task UpsertAsync(SampleReadModel document, CancellationToken cancellationToken = default)
  {
    var collection = GetCollection();
    var filter = Builders<SampleDocument>.Filter.Eq(d => d.SampleId, document.SampleId);
    var replacement = new SampleDocument
    {
      SampleId = document.SampleId,
      Name = document.Name,
      Status = document.Status,
      Description = document.Description,
      LastUpdated = document.LastUpdated
    };
    var options = new ReplaceOptions { IsUpsert = true };

    _logger.LogDebug("Upserting Sample read model {SampleId}", document.SampleId);
    await collection.ReplaceOneAsync(filter, replacement, options, cancellationToken);
  }

  public async Task DeleteAsync(int sampleId, CancellationToken cancellationToken = default)
  {
    var collection = GetCollection();
    var filter = Builders<SampleDocument>.Filter.Eq(d => d.SampleId, sampleId);

    _logger.LogDebug("Deleting Sample read model {SampleId}", sampleId);
    await collection.DeleteOneAsync(filter, cancellationToken);
  }
}
