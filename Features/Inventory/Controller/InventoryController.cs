namespace LinenLady.Inventory.API.Controllers;

using LinenLady.API.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Inventory.Availability.Handler;
using LinenLady.API.Inventory.Items.Handler;
using LinenLady.API.Inventory.Sql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/items")]
public sealed class InventoryController(
    IInventoryRepository repo,
    GetItemsHandler listHandler,
    UpdateItemHandler updateHandler,
    SoftDeleteItemHandler deleteHandler,
    CreateItemsHandler createHandler,
    GetAvailabilityHandler availabilityHandler,
    IAuthorizationService authorization) : ControllerBase
{
    /// <summary>
    /// True when the current caller satisfies the Admin policy (Clerk JWT
    /// with org_id == Clerk:AdminOrgId). Used to gate which inventory
    /// statuses are visible on the otherwise-anonymous read endpoints —
    /// drafts must never be served to non-admin callers.
    /// </summary>
    private async Task<bool> IsAdminAsync()
        => (await authorization.AuthorizeAsync(User, AuthPolicies.Admin)).Succeeded;

    // POST /items/drafts
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPost("drafts")]
    public async Task<IActionResult> CreateDrafts(
        [FromBody] CreateItemsRequest? body,
        CancellationToken ct)
    {
        if (body is null) return BadRequest("Invalid JSON body.");

        var result = await createHandler.HandleAsync(body, ct);
        return Ok(result);
    }

    // GET /items  — public storefront
    //
    // SECURITY: this endpoint is anonymous, but `status` is an admin-grade
    // filter. Non-admin callers are clamped to the published catalog —
    // "active" and "featured" only. Requests for "draft" or "all" from a
    // non-admin are coerced to "active" rather than rejected so a stale
    // storefront query string degrades gracefully instead of breaking the
    // shop page. Admin callers (valid Clerk JWT in the admin org) keep the
    // full status set for the dashboard.
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetItems(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 10,
        [FromQuery] string status = "active",
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        var requested = (status ?? "active").Trim().ToLowerInvariant();

        if (requested is "draft" or "all" && !await IsAdminAsync())
            requested = "active";

        var (result, response) = await listHandler.Handle(new GetItemsQuery
        {
            Page = page,
            Limit = limit,
            Status = requested,
            Category = category
        }, ct);

        return result switch
        {
            GetItemsResult.Ok => Ok(response),
            GetItemsResult.BadRequest => BadRequest("Invalid status. Use: all, draft, active, featured."),
            _ => StatusCode(500, "Unexpected outcome.")
        };
    }

    // GET /items/counts  — admin dashboard metric
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpGet("counts")]
    public async Task<IActionResult> GetCounts(CancellationToken ct)
    {
        var counts = await repo.GetCounts(ct);
        return Ok(counts);
    }

    // GET /items/{id:int}  — public storefront
    //
    // Drafts 404 for non-admin callers — same response as a nonexistent id
    // so draft ids aren't enumerable. Inactive (sold) items remain visible
    // by direct link so old shares/order references still resolve.
    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest("Invalid id.");

        var item = await repo.GetByKey(ItemKey.ById(id), ct);
        if (item is null || (item.IsDraft && !await IsAdminAsync()))
            return NotFound("Item not found.");

        return Ok(item);
    }

    // GET /items/sku/{sku}  — public storefront
    // Same draft gate as GetById.
    [AllowAnonymous]
    [HttpGet("sku/{sku}")]
    public async Task<IActionResult> GetBySku(string sku, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sku)) return BadRequest("Invalid sku.");

        var item = await repo.GetByKey(ItemKey.BySku(sku), ct);
        if (item is null || (item.IsDraft && !await IsAdminAsync()))
            return NotFound("Item not found.");

        return Ok(item);
    }

    // PATCH /items/{id:int}
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> UpdateItem(
        int id,
        [FromBody] UpdateItemRequest? body,
        CancellationToken ct)
    {
        if (id <= 0 || body is null) return BadRequest();

        var (result, response) = await updateHandler.Handle(id, body, ct);
        return result switch
        {
            UpdateItemResult.Updated    => Ok(response),
            UpdateItemResult.NotFound   => NotFound(),
            UpdateItemResult.BadRequest => BadRequest(),
            _ => StatusCode(500)
        };
    }

    // DELETE /items/{id:int}
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> SoftDelete(int id, CancellationToken ct)
    {
        var result = await deleteHandler.Handle(id, ct);
        return result switch
        {
            SoftDeleteItemResult.Deleted  => NoContent(),
            SoftDeleteItemResult.NotFound => NotFound(),
            _ => StatusCode(500)
        };
    }


    // Returns availability state for the given inventory ids. Items not
    // present in the response are implicitly available. Public access; if
    // a JWT is present we pass the Clerk user id through so the handler
    // can surface YourBasket / YourPendingPayment for the caller's own
    // blocks, but the caller-resolution only happens when a block is
    // actually detected (saves a DB round-trip on the common all-available
    // case).
    [AllowAnonymous]
    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(
        [FromQuery(Name = "ids")] string? ids,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return Ok(new GetAvailabilityResponse());

        var parsed = ids
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .ToList();

        if (parsed.Count == 0)
            return Ok(new GetAvailabilityResponse());

        if (parsed.Count > 200)
            return BadRequest("Too many ids (max 200 per request).");

        // Pull the Clerk user id from the JWT if the caller is authenticated.
        // The handler does the customer-id resolution lazily — only if at
        // least one item is blocked, so anonymous callers and "everything
        // available" callers both skip the customer lookup entirely.
        var clerkUserId = User?.Identity?.IsAuthenticated == true
            ? User.FindFirst("sub")?.Value
            : null;

        var resp = await availabilityHandler.Handle(
            new GetAvailabilityRequest { InventoryIds = parsed },
            clerkUserId,
            ct);

        return Ok(resp);
    }
}