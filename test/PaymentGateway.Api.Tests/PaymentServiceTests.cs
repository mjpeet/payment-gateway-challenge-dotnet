using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using PaymentGateway.Api.BankClient;
using PaymentGateway.Api.Idempotency;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Observability;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class PaymentServiceTests
{
    private readonly Mock<IAcquiringBankClient> _bankClient = new();
    private readonly Mock<IPaymentsRepository> _repository = new();
    private readonly Mock<IIdempotencyStore> _idempotencyStore = new();
    private readonly IIdempotencyKeyLock _idempotencyKeyLock = new IdempotencyKeyLock();
    private readonly Mock<IPaymentGatewayMetrics> _metrics = new();
    private readonly PaymentService _sut;

    public PaymentServiceTests()
    {
        _sut = new PaymentService(
            _bankClient.Object,
            _repository.Object,
            _idempotencyStore.Object,
            _idempotencyKeyLock,
            _metrics.Object,
            NullLogger<PaymentService>.Instance);
    }

    private static ProcessPaymentCommand ValidCommand() =>
        new("4111111111111111", 12, DateTime.UtcNow.Year + 5, "GBP", 100, "123");

    [Fact]
    public async Task ProcessAsync_BankAuthorizes_PersistsAuthorizedPaymentAndReturnsIt()
    {
        _bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAuthorizationResult(true, "auth-code"));

        var outcome = await _sut.ProcessAsync(ValidCommand(), idempotencyKey: null, CancellationToken.None);

        var processed = Assert.IsType<PaymentProcessed>(outcome);
        Assert.Equal(PaymentStatus.Authorized, processed.Payment.Status);
        _repository.Verify(r => r.Add(It.Is<Payment>(p => p.Status == PaymentStatus.Authorized)), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_BankDeclines_PersistsDeclinedPayment()
    {
        _bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAuthorizationResult(false, null));

        var outcome = await _sut.ProcessAsync(ValidCommand(), idempotencyKey: null, CancellationToken.None);

        var processed = Assert.IsType<PaymentProcessed>(outcome);
        Assert.Equal(PaymentStatus.Declined, processed.Payment.Status);
        _repository.Verify(r => r.Add(It.Is<Payment>(p => p.Status == PaymentStatus.Declined)), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_BankUnavailable_DoesNotPersistAnythingAndReturnsBankUnavailable()
    {
        _bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AcquiringBankUnavailableException("down"));

        var outcome = await _sut.ProcessAsync(ValidCommand(), idempotencyKey: null, CancellationToken.None);

        Assert.IsType<BankUnavailable>(outcome);
        _repository.Verify(r => r.Add(It.IsAny<Payment>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_NoIdempotencyKey_NeverTouchesIdempotencyStore()
    {
        _bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAuthorizationResult(true, "auth-code"));

        await _sut.ProcessAsync(ValidCommand(), idempotencyKey: null, CancellationToken.None);

        _idempotencyStore.Verify(s => s.Get(It.IsAny<string>()), Times.Never);
        _idempotencyStore.Verify(s => s.Save(It.IsAny<string>(), It.IsAny<IdempotencyRecord>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_NewIdempotencyKey_ProcessesNormallyAndSavesResult()
    {
        _idempotencyStore.Setup(s => s.Get("key-1")).Returns((IdempotencyRecord?)null);
        _bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAuthorizationResult(true, "auth-code"));

        var outcome = await _sut.ProcessAsync(ValidCommand(), "key-1", CancellationToken.None);

        Assert.IsType<PaymentProcessed>(outcome);
        _idempotencyStore.Verify(s => s.Save("key-1", It.IsAny<IdempotencyRecord>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_RepeatedKeySamePayload_SecondCallReplaysWithoutCallingBankAgain()
    {
        var command = ValidCommand();
        IdempotencyRecord? savedRecord = null;

        _idempotencyStore.Setup(s => s.Get("key-1")).Returns(() => savedRecord);
        _idempotencyStore
            .Setup(s => s.Save("key-1", It.IsAny<IdempotencyRecord>()))
            .Callback<string, IdempotencyRecord>((_, record) => savedRecord = record);

        _bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAuthorizationResult(true, "auth-code"));

        var firstOutcome = await _sut.ProcessAsync(command, "key-1", CancellationToken.None);
        var secondOutcome = await _sut.ProcessAsync(command, "key-1", CancellationToken.None);

        var firstPayment = Assert.IsType<PaymentProcessed>(firstOutcome).Payment;
        var secondPayment = Assert.IsType<PaymentProcessed>(secondOutcome).Payment;

        Assert.Equal(firstPayment.Id, secondPayment.Id);
        _bankClient.Verify(
            b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.Add(It.IsAny<Payment>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_RepeatedKeyDifferentPayload_ReturnsConflictWithoutCallingBankAgain()
    {
        var firstCommand = ValidCommand();
        var secondCommand = firstCommand with { Amount = firstCommand.Amount + 1 };
        IdempotencyRecord? savedRecord = null;

        _idempotencyStore.Setup(s => s.Get("key-1")).Returns(() => savedRecord);
        _idempotencyStore
            .Setup(s => s.Save("key-1", It.IsAny<IdempotencyRecord>()))
            .Callback<string, IdempotencyRecord>((_, record) => savedRecord = record);

        _bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAuthorizationResult(true, "auth-code"));

        await _sut.ProcessAsync(firstCommand, "key-1", CancellationToken.None);
        var secondOutcome = await _sut.ProcessAsync(secondCommand, "key-1", CancellationToken.None);

        Assert.IsType<IdempotencyConflict>(secondOutcome);
        _bankClient.Verify(
            b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ConcurrentCallsSameNewKey_OnlyCallsBankOnceAndBothReturnSamePayment()
    {
        var command = ValidCommand();
        IdempotencyRecord? savedRecord = null;
        var recordLock = new object();

        _idempotencyStore.Setup(s => s.Get("key-1")).Returns(() =>
        {
            lock (recordLock)
            {
                return savedRecord;
            }
        });
        _idempotencyStore
            .Setup(s => s.Save("key-1", It.IsAny<IdempotencyRecord>()))
            .Callback<string, IdempotencyRecord>((_, record) =>
            {
                lock (recordLock)
                {
                    savedRecord = record;
                }
            });

        _bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAuthorizationResult(true, "auth-code"));

        var results = await Task.WhenAll(
            _sut.ProcessAsync(command, "key-1", CancellationToken.None),
            _sut.ProcessAsync(command, "key-1", CancellationToken.None));

        var firstPayment = Assert.IsType<PaymentProcessed>(results[0]).Payment;
        var secondPayment = Assert.IsType<PaymentProcessed>(results[1]).Payment;

        Assert.Equal(firstPayment.Id, secondPayment.Id);
        _bankClient.Verify(
            b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.Add(It.IsAny<Payment>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ConcurrentCallsDifferentKeys_BothCallBankIndependently()
    {
        _idempotencyStore.Setup(s => s.Get(It.IsAny<string>())).Returns((IdempotencyRecord?)null);
        _bankClient
            .Setup(b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankAuthorizationResult(true, "auth-code"));

        await Task.WhenAll(
            _sut.ProcessAsync(ValidCommand(), "key-1", CancellationToken.None),
            _sut.ProcessAsync(ValidCommand(), "key-2", CancellationToken.None));

        _bankClient.Verify(
            b => b.AuthorizeAsync(It.IsAny<BankAuthorizationRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
