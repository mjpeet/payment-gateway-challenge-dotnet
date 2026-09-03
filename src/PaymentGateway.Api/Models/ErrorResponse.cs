namespace PaymentGateway.Api.Models;

public class ErrorResponse
{
    public List<ValidationError> Errors { get; init; } = new();
}
