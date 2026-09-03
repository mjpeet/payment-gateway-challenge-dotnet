using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PaymentGateway.Api.BankClient;
using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.TestDoubles;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    private readonly Random _random = new();

    [Fact]
    public async Task RetrievesAPaymentSuccessfully()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Status = PaymentStatus.Authorized,
            ExpiryYear = _random.Next(2023, 2030),
            ExpiryMonth = _random.Next(1, 12),
            Amount = _random.Next(1, 10000),
            CardNumberLastFour = _random.Next(1111, 9999).ToString(),
            Currency = "GBP",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var paymentsRepository = new PaymentsRepository();
        paymentsRepository.Add(payment);

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IPaymentsRepository>(paymentsRepository)))
            .CreateClient();

        var response = await client.GetAsync($"/api/Payments/{payment.Id}");
        var paymentResponse = await response.Content.ReadFromJsonAsync<GetPaymentResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
    }

    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.CreateClient();

        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpClient CreateClientWithStubBank(StubAcquiringBankClient stubBankClient) =>
        new WebApplicationFactory<PaymentsController>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAcquiringBankClient>();
                services.AddSingleton<IAcquiringBankClient>(stubBankClient);
            }))
            .CreateClient();

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "4111111111111111",
        ExpiryMonth = 12,
        ExpiryYear = DateTime.UtcNow.Year + 5,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    [Fact]
    public async Task PostPayment_ValidRequest_Returns201WithLocationHeader()
    {
        var stubBankClient = new StubAcquiringBankClient
        {
            OnAuthorize = _ => new BankAuthorizationResult(true, "auth-code")
        };
        var client = CreateClientWithStubBank(stubBankClient);

        var response = await client.PostAsJsonAsync("/api/Payments", ValidRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task PostPayment_InvalidRequest_Returns400AndNeverCallsBank()
    {
        var stubBankClient = new StubAcquiringBankClient();
        var client = CreateClientWithStubBank(stubBankClient);
        var invalidRequest = ValidRequest();
        invalidRequest.CardNumber = "123";

        var response = await client.PostAsJsonAsync("/api/Payments", invalidRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, stubBankClient.CallCount);
    }

    [Fact]
    public async Task PostPayment_BankUnavailable_Returns502()
    {
        var stubBankClient = new StubAcquiringBankClient
        {
            ExceptionToThrow = new AcquiringBankUnavailableException("down")
        };
        var client = CreateClientWithStubBank(stubBankClient);

        var response = await client.PostAsJsonAsync("/api/Payments", ValidRequest());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task PostPayment_IdempotentReplay_ReturnsSameResponseAndDoesNotCallBankTwice()
    {
        var stubBankClient = new StubAcquiringBankClient
        {
            OnAuthorize = _ => new BankAuthorizationResult(true, "auth-code")
        };
        var client = CreateClientWithStubBank(stubBankClient);

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(ValidRequest())
        };
        firstRequest.Headers.Add("Idempotency-Key", "key-1");
        var firstResponse = await client.SendAsync(firstRequest);

        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(ValidRequest())
        };
        secondRequest.Headers.Add("Idempotency-Key", "key-1");
        var secondResponse = await client.SendAsync(secondRequest);

        var firstBody = await firstResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal(firstBody!.Id, secondBody!.Id);
        Assert.Equal(1, stubBankClient.CallCount);
    }

    [Fact]
    public async Task PostPayment_IdempotencyKeyReusedWithDifferentPayload_Returns409()
    {
        var stubBankClient = new StubAcquiringBankClient
        {
            OnAuthorize = _ => new BankAuthorizationResult(true, "auth-code")
        };
        var client = CreateClientWithStubBank(stubBankClient);

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(ValidRequest())
        };
        firstRequest.Headers.Add("Idempotency-Key", "key-1");
        await client.SendAsync(firstRequest);

        var differentRequest = ValidRequest();
        differentRequest.Amount = 999;
        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/Payments")
        {
            Content = JsonContent.Create(differentRequest)
        };
        secondRequest.Headers.Add("Idempotency-Key", "key-1");
        var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(1, stubBankClient.CallCount);
    }
}
