using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Services;

public sealed record PaymentProcessed(Payment Payment) : ProcessPaymentOutcome;
