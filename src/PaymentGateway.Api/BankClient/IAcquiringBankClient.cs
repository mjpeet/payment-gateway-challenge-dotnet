namespace PaymentGateway.Api.BankClient;

public interface IAcquiringBankClient
{
    Task<BankAuthorizationResult> AuthorizeAsync(
        BankAuthorizationRequest request, CancellationToken cancellationToken);
}
