namespace PaymentGateway.Api.Services;

public interface IPaymentService
{
    Task<ProcessPaymentOutcome> ProcessAsync(
        ProcessPaymentCommand command, string? idempotencyKey, CancellationToken cancellationToken);
}
