namespace PaymentGateway.Api.Observability;

public interface IPaymentGatewayMetrics
{
    // outcome: "Authorized" | "Declined" | "Rejected" | "BankUnavailable" | "IdempotencyConflict"
    void RecordPaymentOutcome(string outcome);

    // result: "authorized" | "declined" | "unavailable"
    void RecordBankCallLatency(double elapsedMilliseconds, string result);
}
