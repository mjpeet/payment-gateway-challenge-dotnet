namespace PaymentGateway.Api.Idempotency;

public interface IIdempotencyStore
{
    IdempotencyRecord? Get(string idempotencyKey);
    void Save(string idempotencyKey, IdempotencyRecord record);
}
