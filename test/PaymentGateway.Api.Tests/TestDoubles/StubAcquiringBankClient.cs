using PaymentGateway.Api.BankClient;

namespace PaymentGateway.Api.Tests.TestDoubles;

internal class StubAcquiringBankClient : IAcquiringBankClient
{
    public int CallCount { get; private set; }
    public Func<BankAuthorizationRequest, BankAuthorizationResult>? OnAuthorize { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    public Task<BankAuthorizationResult> AuthorizeAsync(
        BankAuthorizationRequest request, CancellationToken cancellationToken)
    {
        CallCount++;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        var result = OnAuthorize?.Invoke(request) ?? new BankAuthorizationResult(true, Guid.NewGuid().ToString());
        return Task.FromResult(result);
    }
}
