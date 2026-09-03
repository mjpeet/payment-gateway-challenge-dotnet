using System.Collections.Concurrent;

namespace PaymentGateway.Api.Idempotency;

public class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new();

    public IdempotencyRecord? Get(string idempotencyKey) =>
        _records.GetValueOrDefault(idempotencyKey);

    public void Save(string idempotencyKey, IdempotencyRecord record) =>
        _records[idempotencyKey] = record;
}
