namespace PaymentGateway.Api.Idempotency;

public record IdempotencyRecord(string RequestPayloadHash, string ResponseBody);
