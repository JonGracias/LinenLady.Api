namespace LinenLady.API.Controllers;

using LinenLady.API.Api.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Customers.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize(Policy = AuthPolicies.Customer)]
[Route("customers/me/messages")]
public sealed class MessagesController(MessageHandler handler) : ControllerBase
{
    // GET /customers/me/messages
    [HttpGet]
    public async Task<IActionResult> GetMessages(CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        var result = await handler.GetAsync(clerkUserId, ct);
        return Ok(result);
    }

    // POST /customers/me/messages
    [HttpPost]
    public async Task<IActionResult> SendMessage(
        [FromBody] SendMessageRequest? body,
        CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();
        if (body is null || string.IsNullOrWhiteSpace(body.Body))
            return BadRequest("Message body is required.");

        var result = await handler.SendAsync(clerkUserId, body, ct);
        return StatusCode(201, result);
    }
}
