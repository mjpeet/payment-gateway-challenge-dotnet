using System.Diagnostics.Metrics;

namespace PaymentGateway.Api.Observability;

public class PaymentGatewayMetrics : IPaymentGatewayMetrics
{
    public const string MeterName = "PaymentGateway.Api";

    private readonly Counter<long> _paymentsProcessed;
    private readonly Histogram<double> _bankCallDurationMs;

    public PaymentGatewayMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _paymentsProcessed = meter.CreateCounter<long>(
            "payments.processed",
            unit: "{payment}",
            description: "Number of payment attempts, tagged by outcome.");

        _bankCallDurationMs = meter.CreateHistogram<double>(
            "bank.call.duration",
            unit: "ms",
            description: "Latency of calls to the acquiring bank, tagged by result.");
    }

    public void RecordPaymentOutcome(string outcome) =>
        _paymentsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordBankCallLatency(double elapsedMilliseconds, string result) =>
        _bankCallDurationMs.Record(elapsedMilliseconds, new KeyValuePair<string, object?>("result", result));
}
