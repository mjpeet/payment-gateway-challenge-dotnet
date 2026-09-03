using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using PaymentGateway.Api.BankClient;
using PaymentGateway.Api.Observability;
using PaymentGateway.Api.Tests.TestDoubles;

namespace PaymentGateway.Api.Tests;

public class AcquiringBankClientTests
{
    private static AcquiringBankClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        return new AcquiringBankClient(
            httpClient, new Mock<IPaymentGatewayMetrics>().Object, NullLogger<AcquiringBankClient>.Instance);
    }

    private static BankAuthorizationRequest ValidRequest(string cardNumber = "4111111111111111") =>
        new(cardNumber, "04/2030", "GBP", 100, "123");

    [Fact]
    public async Task AuthorizeAsync_BankAuthorizes_ReturnsAuthorizedResult()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { authorized = true, authorization_code = "abc-123" })
        });
        var client = CreateClient(handler);

        var result = await client.AuthorizeAsync(ValidRequest(), CancellationToken.None);

        Assert.True(result.Authorized);
        Assert.Equal("abc-123", result.AuthorizationCode);
    }

    [Fact]
    public async Task AuthorizeAsync_BankDeclines_ReturnsDeclinedResult()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { authorized = false, authorization_code = (string?)null })
        });
        var client = CreateClient(handler);

        var result = await client.AuthorizeAsync(ValidRequest("4111111111111112"), CancellationToken.None);

        Assert.False(result.Authorized);
        Assert.Null(result.AuthorizationCode);
    }

    [Fact]
    public async Task AuthorizeAsync_BankReturns503_ThrowsAcquiringBankUnavailableException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<AcquiringBankUnavailableException>(() =>
            client.AuthorizeAsync(ValidRequest("4111111111111110"), CancellationToken.None));
    }

    [Fact]
    public async Task AuthorizeAsync_ConnectionFails_ThrowsAcquiringBankUnavailableException()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<AcquiringBankUnavailableException>(() =>
            client.AuthorizeAsync(ValidRequest(), CancellationToken.None));
    }
}
