using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using PaymentGateway.Api.Observability;

namespace PaymentGateway.Api.BankClient;

public class AcquiringBankClient : IAcquiringBankClient
{
    private readonly HttpClient _httpClient;
    private readonly IPaymentGatewayMetrics _metrics;
    private readonly ILogger<AcquiringBankClient> _logger;

    public AcquiringBankClient(
        HttpClient httpClient, IPaymentGatewayMetrics metrics, ILogger<AcquiringBankClient> logger)
    {
        _httpClient = httpClient;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<BankAuthorizationResult> AuthorizeAsync(
        BankAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var simulatorRequest = new SimulatorRequest(
            request.CardNumber,
            request.ExpiryDate,
            request.Currency,
            request.Amount,
            request.Cvv);

        var stopwatch = Stopwatch.StartNew();

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/payments", simulatorRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _metrics.RecordBankCallLatency(stopwatch.Elapsed.TotalMilliseconds, "unavailable");
            _logger.LogError(ex, "Could not reach the acquiring bank");
            throw new AcquiringBankUnavailableException("Could not reach the acquiring bank.", ex);
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _metrics.RecordBankCallLatency(stopwatch.Elapsed.TotalMilliseconds, "unavailable");
            _logger.LogWarning("Acquiring bank returned 503 Service Unavailable");
            throw new AcquiringBankUnavailableException("Acquiring bank returned 503 Service Unavailable.");
        }

        response.EnsureSuccessStatusCode();

        var simulatorResponse = await response.Content
            .ReadFromJsonAsync<SimulatorResponse>(cancellationToken: cancellationToken)
            ?? throw new AcquiringBankUnavailableException("Acquiring bank returned an empty response body.");

        _metrics.RecordBankCallLatency(
            stopwatch.Elapsed.TotalMilliseconds, simulatorResponse.Authorized ? "authorized" : "declined");

        return new BankAuthorizationResult(simulatorResponse.Authorized, simulatorResponse.AuthorizationCode);
    }

    private sealed record SimulatorRequest(
        [property: JsonPropertyName("card_number")] string CardNumber,
        [property: JsonPropertyName("expiry_date")] string ExpiryDate,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("amount")] int Amount,
        [property: JsonPropertyName("cvv")] string Cvv);

    private sealed record SimulatorResponse(
        [property: JsonPropertyName("authorized")] bool Authorized,
        [property: JsonPropertyName("authorization_code")] string? AuthorizationCode);
}
