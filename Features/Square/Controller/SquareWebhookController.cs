namespace LinenLady.API.Controllers;

using LinenLady.API.Square;
using LinenLady.API.Square.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

[ApiController]
public sealed class SquareWebhookController(
    SquareWebhookHandler handler,
    IOptions<SquareOptions> squareOptions) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("api/square/webhook")]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(body))
            return BadRequest();

        var opts = squareOptions.Value;
        var signature = Request.Headers["x-square-hmacsha256-signature"].FirstOrDefault();

        if (!SquareWebhookSignature.IsValid(
                opts.WebhookSignatureKey,
                opts.WebhookNotificationUrl,
                body,
                signature))
        {
            return Unauthorized();
        }

        await handler.HandleAsync(body, ct);
        return Ok();
    }
}