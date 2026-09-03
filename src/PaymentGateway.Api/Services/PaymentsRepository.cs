using System.Collections.Concurrent;
using PaymentGateway.Api.Models;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository : IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, Payment> _payments = new();

    public void Add(Payment payment) => _payments[payment.Id] = payment;

    public Payment? Get(Guid id) => _payments.GetValueOrDefault(id);
}
