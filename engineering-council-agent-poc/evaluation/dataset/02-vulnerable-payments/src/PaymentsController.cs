using Microsoft.AspNetCore.Mvc;

namespace VulnerablePayments;

// Intentionally vulnerable fixture — do not copy.
[ApiController]
[Route("payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly AccountRepository _accounts = new();

    // No [Authorize]: refunds can be triggered anonymously (missing authorization).
    [HttpPost("refund")]
    public IActionResult Refund([FromQuery] string account, [FromQuery] decimal amount)
    {
        var id = _accounts.FindByName(account);
        return Ok(new { refunded = amount, account = id });
    }
}
