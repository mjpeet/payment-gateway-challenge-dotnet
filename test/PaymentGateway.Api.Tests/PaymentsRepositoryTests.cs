using PaymentGateway.Api.Models;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentsRepositoryTests
{
    private static Payment CreatePayment(Guid id) => new()
    {
        Id = id,
        Status = PaymentStatus.Authorized,
        CardNumberLastFour = "1111",
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 100,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void AddThenGet_ReturnsTheSamePayment()
    {
        var repository = new PaymentsRepository();
        var payment = CreatePayment(Guid.NewGuid());

        repository.Add(payment);
        var retrieved = repository.Get(payment.Id);

        Assert.Same(payment, retrieved);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var repository = new PaymentsRepository();

        var retrieved = repository.Get(Guid.NewGuid());

        Assert.Null(retrieved);
    }

    [Fact]
    public void Add_TwoPayments_BothRetrievableIndependently()
    {
        var repository = new PaymentsRepository();
        var first = CreatePayment(Guid.NewGuid());
        var second = CreatePayment(Guid.NewGuid());

        repository.Add(first);
        repository.Add(second);

        Assert.Same(first, repository.Get(first.Id));
        Assert.Same(second, repository.Get(second.Id));
    }

    [Fact]
    public async Task Add_CalledConcurrentlyFromManyThreads_AllPaymentsPersistedWithoutLoss()
    {
        var repository = new PaymentsRepository();
        var payments = Enumerable.Range(0, 500)
            .Select(_ => CreatePayment(Guid.NewGuid()))
            .ToList();

        await Task.WhenAll(payments.Select(payment => Task.Run(() => repository.Add(payment))));

        Assert.All(payments, payment => Assert.Same(payment, repository.Get(payment.Id)));
    }
}
