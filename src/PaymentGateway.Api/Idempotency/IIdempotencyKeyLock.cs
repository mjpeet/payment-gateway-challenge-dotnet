namespace PaymentGateway.Api.Idempotency;

public interface IIdempotencyKeyLock
{
    Task<IDisposable> AcquireAsync(string idempotencyKey, CancellationToken cancellationToken);
}
