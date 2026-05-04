using Hex.Scaffold.Domain.AccountAggregate;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Hex.Scaffold.Adapters.Persistence.MongoDb;

// Vogen value-object adapter for the BSON serializer. AccountId wraps a
// string ("acct_…"); we round-trip it through the BSON wire as a plain
// UTF-8 string and reconstruct via AccountId.From — that path runs the
// validator, so a tampered document raises ValueObjectValidationException
// at read time instead of producing an aggregate with an invalid Id.
public sealed class AccountIdBsonSerializer : SerializerBase<AccountId>
{
  public override AccountId Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    => AccountId.From(context.Reader.ReadString());

  public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, AccountId value)
    => context.Writer.WriteString(value.Value);
}
