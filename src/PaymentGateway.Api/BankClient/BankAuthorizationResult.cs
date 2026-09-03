namespace PaymentGateway.Api.BankClient;

public record BankAuthorizationResult(bool Authorized, string? AuthorizationCode);
