using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Observability;

namespace PaymentGateway.Api.Tests;

public class ObservabilityTests
{
    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var client = new WebApplicationFactory<PaymentsController>().CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CorrelationId_WhenProvided_IsEchoedBackOnTheResponse()
    {
        var client = new WebApplicationFactory<PaymentsController>().CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/Payments/{Guid.NewGuid()}");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "test-correlation-id");

        var response = await client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        Assert.Equal("test-correlation-id", values!.Single());
    }

    [Fact]
    public async Task CorrelationId_WhenNotProvided_OneIsGeneratedOnTheResponse()
    {
        var client = new WebApplicationFactory<PaymentsController>().CreateClient();

        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        Assert.True(response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values));
        Assert.False(string.IsNullOrWhiteSpace(values!.Single()));
    }
}
