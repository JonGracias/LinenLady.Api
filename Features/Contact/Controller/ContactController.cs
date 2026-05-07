namespace LinenLady.Api.Features.Contact.Handler;

using LinenLady.Api.Features.Contact.Contracts;
using LinenLady.Api.Features.Contact.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/contact")]
[AllowAnonymous]
public sealed class ContactController(IContactService service) : ControllerBase
{
    private readonly IContactService _service = service;

    /// <summary>
    /// Public "Contact Noemi" submission. No auth.
    /// Validation handled by [ApiController] model binding; rate limits enforced inside the service.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ContactResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ContactResponse>> Submit(
        [FromBody] ContactRequest request,
        CancellationToken ct)
    {
        var ip        = GetClientIp();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _service.SubmitAsync(request, ip, userAgent, ct);
        return Ok(result);
    }

    /// <summary>
    /// Resolves the originating IP. Behind Azure Front Door / App Service the real
    /// client lives in X-Forwarded-For. Trust only the leftmost entry.
    /// </summary>
    private string? GetClientIp()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var xff) && xff.Count > 0)
        {
            var first = xff.ToString().Split(',', 2)[0].Trim();
            if (!string.IsNullOrEmpty(first)) return first;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
