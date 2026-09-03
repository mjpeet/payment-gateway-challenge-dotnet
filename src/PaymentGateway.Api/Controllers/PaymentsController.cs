using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Mapping;
using PaymentGateway.Api.Models;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Observability;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : Controller
{
    private readonly IPaymentsRepository _paymentsRepository;
    private readonly IPaymentService _paymentService;
    private readonly PostPaymentRequestValidator _validator;
    private readonly IPaymentGatewayMetrics _metrics;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentsRepository paymentsRepository,
        IPaymentService paymentService,
        PostPaymentRequestValidator validator,
        IPaymentGatewayMetrics metrics,
        ILogger<PaymentsController> logger)
    {
        _paymentsRepository = paymentsRepository;
        _paymentService = paymentService;
        _validator = validator;
        _metrics = metrics;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public ActionResult<GetPaymentResponse> GetPaymentAsync(Guid id)
    {
        var payment = _paymentsRepository.Get(id);

        if (payment is null)
        {
            _logger.LogWarning("Payment {PaymentId} not found", id);
            return NotFound();
        }

        return Ok(payment.ToGetPaymentResponse());
    }

    [HttpPost]
    public async Task<IActionResult> PostPaymentAsync(
        [FromBody] PostPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var errors = _validator.Validate(request);
        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "Payment request rejected: {Fields}", string.Join(", ", errors.Select(e => e.Field)));
            _metrics.RecordPaymentOutcome("Rejected");
            return BadRequest(new ErrorResponse { Errors = errors });
        }

        var command = new ProcessPaymentCommand(
            request.CardNumber,
            request.ExpiryMonth,
            request.ExpiryYear,
            request.Currency,
            request.Amount,
            request.Cvv);

        var outcome = await _paymentService.ProcessAsync(command, idempotencyKey, cancellationToken);

        return outcome switch
        {
            PaymentProcessed processed => CreatedAtAction(
                "GetPayment",
                new { id = processed.Payment.Id },
                processed.Payment.ToPostPaymentResponse()),

            IdempotencyConflict => LogAndReturnConflict(idempotencyKey),
            BankUnavailable => StatusCode(StatusCodes.Status502BadGateway),

            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }
    private IActionResult LogAndReturnConflict(string? idempotencyKey)
    {
        _logger.LogWarning(
            "Idempotency key {IdempotencyKey} reused with a different request payload", idempotencyKey);
        _metrics.RecordPaymentOutcome("IdempotencyConflict");

        return Conflict(new ErrorResponse
            {
                Errors = new List<ValidationError>
                {
                    new()
                    {
                        Field = "Idempotency-Key",
                        Message = "This key has already been used with a different request."
                    }
                }
        });
    }
}
