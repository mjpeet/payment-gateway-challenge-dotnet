using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Tests;

public class PostPaymentRequestValidatorTests
{
    private readonly PostPaymentRequestValidator _validator = new();

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
    public void Validate_FullyValidRequest_ReturnsNoErrors()
    {
        var errors = _validator.Validate(ValidRequest());

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("1234567890123")]
    [InlineData("123456789012345678901")]
    [InlineData("41111111111111ab")]
    public void Validate_InvalidCardNumber_ReturnsError(string cardNumber)
    {
        var request = ValidRequest();
        request.CardNumber = cardNumber;

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Field == "cardNumber");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Validate_ExpiryMonthOutOfRange_ReturnsError(int month)
    {
        var request = ValidRequest();
        request.ExpiryMonth = month;

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Field == "expiryMonth");
    }

    [Fact]
    public void Validate_ExpiryInThePast_ReturnsError()
    {
        var request = ValidRequest();
        request.ExpiryMonth = 1;
        request.ExpiryYear = DateTime.UtcNow.Year - 1;

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Field == "expiryYear");
    }

    [Fact]
    public void Validate_ExpiryIsCurrentMonth_DoesNotReturnExpiryError()
    {
        var now = DateTime.UtcNow;
        var request = ValidRequest();
        request.ExpiryMonth = now.Month;
        request.ExpiryYear = now.Year;

        var errors = _validator.Validate(request);

        Assert.DoesNotContain(errors, e => e.Field == "expiryYear");
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("JPY")] 
    public void Validate_UnsupportedCurrency_ReturnsError(string currency)
    {
        var request = ValidRequest();
        request.Currency = currency;

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Field == "currency");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Validate_NonPositiveAmount_ReturnsError(int amount)
    {
        var request = ValidRequest();
        request.Amount = amount;

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Field == "amount");
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("12a")]
    public void Validate_InvalidCvv_ReturnsError(string cvv)
    {
        var request = ValidRequest();
        request.Cvv = cvv;

        var errors = _validator.Validate(request);

        Assert.Contains(errors, e => e.Field == "cvv");
    }

    [Theory]
    [InlineData("12345678901234")]
    [InlineData("1234567890123456789")]
    public void Validate_CardNumberAtLengthBoundary_ReturnsNoCardNumberError(string cardNumber)
    {
        var request = ValidRequest();
        request.CardNumber = cardNumber;

        var errors = _validator.Validate(request);

        Assert.DoesNotContain(errors, e => e.Field == "cardNumber");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Validate_ExpiryMonthAtBoundary_ReturnsNoExpiryMonthError(int month)
    {
        var request = ValidRequest();
        request.ExpiryMonth = month;

        var errors = _validator.Validate(request);

        Assert.DoesNotContain(errors, e => e.Field == "expiryMonth");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234")]
    public void Validate_CvvAtLengthBoundary_ReturnsNoCvvError(string cvv)
    {
        var request = ValidRequest();
        request.Cvv = cvv;

        var errors = _validator.Validate(request);

        Assert.DoesNotContain(errors, e => e.Field == "cvv");
    }

    [Fact]
    public void Validate_SmallestPositiveAmount_ReturnsNoAmountError()
    {
        var request = ValidRequest();
        request.Amount = 1;

        var errors = _validator.Validate(request);

        Assert.DoesNotContain(errors, e => e.Field == "amount");
    }

    [Theory]
    [InlineData("GBP")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void Validate_EachSupportedCurrency_ReturnsNoCurrencyError(string currency)
    {
        var request = ValidRequest();
        request.Currency = currency;

        var errors = _validator.Validate(request);

        Assert.DoesNotContain(errors, e => e.Field == "currency");
    }
}
