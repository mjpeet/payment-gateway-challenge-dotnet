using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Validation;

public class PostPaymentRequestValidator
{
    private static readonly HashSet<string> SupportedCurrencies = new() { "GBP", "USD", "EUR" };

    public List<ValidationError> Validate(PostPaymentRequest request)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrEmpty(request.CardNumber) ||
            request.CardNumber.Length is < 14 or > 19 ||
            !request.CardNumber.All(char.IsDigit))
        {
            errors.Add(new ValidationError
            {
                Field = "cardNumber",
                Message = "Must be 14-19 numeric digits."
            });
        }

        if (request.ExpiryMonth is < 1 or > 12)
        {
            errors.Add(new ValidationError
            {
                Field = "expiryMonth",
                Message = "Must be between 1 and 12."
            });
        }

        if (!IsExpiryInFuture(request.ExpiryMonth, request.ExpiryYear))
        {
            errors.Add(new ValidationError
            {
                Field = "expiryYear",
                Message = "Card expiry (month + year) must be in the future."
            });
        }

        if (string.IsNullOrEmpty(request.Currency) ||
            request.Currency.Length != 3 ||
            !SupportedCurrencies.Contains(request.Currency))
        {
            errors.Add(new ValidationError
            {
                Field = "currency",
                Message = $"Must be one of: {string.Join(", ", SupportedCurrencies)}."
            });
        }

        if (request.Amount <= 0)
        {
            errors.Add(new ValidationError
            {
                Field = "amount",
                Message = "Must be a positive integer (minor currency unit)."
            });
        }

        if (string.IsNullOrEmpty(request.Cvv) ||
            request.Cvv.Length is < 3 or > 4 ||
            !request.Cvv.All(char.IsDigit))
        {
            errors.Add(new ValidationError
            {
                Field = "cvv",
                Message = "Must be 3-4 numeric digits."
            });
        }

        return errors;
    }

    private static bool IsExpiryInFuture(int month, int year)
    {
        if (month is < 1 or > 12) return false;
        if (year is < 1 or > 9999) return false;

        var lastDayOfExpiryMonth = new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        return lastDayOfExpiryMonth >= DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
