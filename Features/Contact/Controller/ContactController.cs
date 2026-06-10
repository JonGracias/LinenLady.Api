namespace LinenLady.API.Features.Contact.Handler;

using LinenLady.API.Features.Contact.Contracts;
using LinenLady.API.Features.Contact.Service;
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
    ///
    /// Client IP comes from HttpContext.Connection.RemoteIpAddress only. Program.cs configures
    /// UseForwardedHeaders so trusted Azure/App Service proxy headers can rewrite RemoteIpAddress.
    /// Do not parse X-Forwarded-For directly here; that header is client-spoofable unless the
    /// forwarded-header middleware has already validated the immediate proxy.
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
        var ip        = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _service.SubmitAsync(request, ip, userAgent, ct);
        return Ok(result);
    }
}
