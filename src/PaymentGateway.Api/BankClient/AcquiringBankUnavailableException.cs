namespace PaymentGateway.Api.BankClient;

public class AcquiringBankUnavailableException : Exception
{
    public AcquiringBankUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
