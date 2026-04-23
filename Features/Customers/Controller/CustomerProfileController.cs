namespace LinenLady.API.Controllers;

using LinenLady.API.Api.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Customers.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize(Policy = AuthPolicies.Customer)]
[Route("api/customers")]
public sealed class CustomerProfileController(
    SyncCustomerHandler syncHandler,
    GetMyProfileHandler getProfileHandler,
    UpdateProfileHandler updateProfileHandler,
    UpsertAddressHandler upsertAddressHandler,
    DeleteAddressHandler deleteAddressHandler,
    SetPreferencesHandler setPreferencesHandler) : ControllerBase
{
    // POST /customers/sync
    // Called on sign-in. Identity fields (ClerkUserId, email, email_verified)
    // come from the validated JWT — the body carries only editable fields.
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(
        [FromBody] UpsertCustomerRequest? body,
        CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        var email = User.GetEmail();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest("JWT is missing an 'email' claim.");

        var isEmailVerified = User.GetEmailVerified();

        var result = await syncHandler.HandleAsync(
            clerkUserId, email, isEmailVerified, body ?? new(null, null, null), ct);
        return Ok(result);
    }

    // GET /customers/me
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        var result = await getProfileHandler.HandleAsync(clerkUserId, ct);
        return Ok(result);
    }

    // PUT /customers/me
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe(
        [FromBody] UpdateCustomerRequest? body,
        CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();
        if (body is null) return BadRequest("Invalid JSON body.");

        var result = await updateProfileHandler.HandleAsync(clerkUserId, body, ct);
        return Ok(result);
    }

    // POST /customers/me/addresses        — new address
    // PUT  /customers/me/addresses/{id}   — update existing
    [HttpPost("me/addresses")]
    [HttpPut("me/addresses/{addressId:int}")]
    public async Task<IActionResult> UpsertAddress(
        int? addressId,
        [FromBody] UpsertAddressRequest? body,
        CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();
        if (body is null) return BadRequest("Invalid JSON body.");

        var result = await upsertAddressHandler.HandleAsync(clerkUserId, body, addressId, ct);
        return Ok(result);
    }

    // DELETE /customers/me/addresses/{id}
    [HttpDelete("me/addresses/{addressId:int}")]
    public async Task<IActionResult> DeleteAddress(int addressId, CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        var deleted = await deleteAddressHandler.HandleAsync(clerkUserId, addressId, ct);
        return deleted ? NoContent() : NotFound();
    }

    // PUT /customers/me/preferences
    [HttpPut("me/preferences")]
    public async Task<IActionResult> SetPreferences(
        [FromBody] SetPreferencesRequest? body,
        CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();
        if (body is null) return BadRequest("Invalid JSON body.");

        var result = await setPreferencesHandler.HandleAsync(clerkUserId, body, ct);
        return Ok(result);
    }
}
