namespace LinenLady.API.Controllers;
 
using LinenLady.API.Customers.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
 
[ApiController]
public sealed class SquareWebhookController(SquareWebhookHandler handler) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("api/square/webhook")]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
 
        if (string.IsNullOrWhiteSpace(body))
            return BadRequest();
 
        await handler.HandleAsync(body, ct);
        return Ok();
    }
}
 