using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaymentGateway.Api.BankClient;
using PaymentGateway.Api.Idempotency;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Observability;

namespace PaymentGateway.Api.Services;

public class PaymentService : IPaymentService
{
    private readonly IAcquiringBankClient _bankClient;
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IIdempotencyKeyLock _idempotencyKeyLock;
    private readonly IPaymentGatewayMetrics _metrics;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IAcquiringBankClient bankClient,
        IPaymentsRepository paymentsRepository,
        IIdempotencyStore idempotencyStore,
        IIdempotencyKeyLock idempotencyKeyLock,
        IPaymentGatewayMetrics metrics,
        ILogger<PaymentService> logger)
    {
        _bankClient = bankClient;
        _paymentsRepository = paymentsRepository;
        _idempotencyStore = idempotencyStore;
        _idempotencyKeyLock = idempotencyKeyLock;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<ProcessPaymentOutcome> ProcessAsync(
        ProcessPaymentCommand command, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (idempotencyKey is null)
        {
            return await ProcessCoreAsync(command, idempotencyKey: null, cancellationToken);
        }

        using var _ = await _idempotencyKeyLock.AcquireAsync(idempotencyKey, cancellationToken);
        return await ProcessCoreAsync(command, idempotencyKey, cancellationToken);
    }

    private async Task<ProcessPaymentOutcome> ProcessCoreAsync(
        ProcessPaymentCommand command, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var payloadHash = ComputeHash(command);

        if (idempotencyKey is not null)
        {
            var existing = _idempotencyStore.Get(idempotencyKey);
            if (existing is not null)
            {
                if (existing.RequestPayloadHash != payloadHash)
                {
                    return new IdempotencyConflict();
                }

                var replayedPayment = JsonSerializer.Deserialize<Payment>(existing.ResponseBody)!;
                _logger.LogInformation(
                    "Idempotent replay for key {IdempotencyKey}, returning payment {PaymentId}",
                    idempotencyKey, replayedPayment.Id);
                return new PaymentProcessed(replayedPayment);
            }
        }

        BankAuthorizationResult bankResult;
        try
        {
            bankResult = await _bankClient.AuthorizeAsync(
                new BankAuthorizationRequest(
                    command.CardNumber,
                    $"{command.ExpiryMonth:D2}/{command.ExpiryYear}",
                    command.Currency,
                    command.Amount,
                    command.Cvv),
                cancellationToken);
        }
        catch (AcquiringBankUnavailableException ex)
        {
            _logger.LogError(ex, "Acquiring bank unavailable — payment not processed");
            _metrics.RecordPaymentOutcome("BankUnavailable");
            return new BankUnavailable();
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = bankResult.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined,
            CardNumberLastFour = command.CardNumber[^4..],
            ExpiryMonth = command.ExpiryMonth,
            ExpiryYear = command.ExpiryYear,
            Currency = command.Currency,
            Amount = command.Amount,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _paymentsRepository.Add(payment);
        _logger.LogInformation("Payment {PaymentId} processed with status {Status}", payment.Id, payment.Status);
        _metrics.RecordPaymentOutcome(payment.Status.ToString());

        if (idempotencyKey is not null)
        {
            _idempotencyStore.Save(
                idempotencyKey,
                new IdempotencyRecord(payloadHash, JsonSerializer.Serialize(payment)));
        }

        return new PaymentProcessed(payment);
    }

    private static string ComputeHash(ProcessPaymentCommand command)
    {
        var json = JsonSerializer.Serialize(command);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
