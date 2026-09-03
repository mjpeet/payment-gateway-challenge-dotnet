namespace PaymentGateway.Api.BankClient;

public record BankAuthorizationRequest(
    string CardNumber,
    string ExpiryDate,
    string Currency,
    int Amount,
    string Cvv);
