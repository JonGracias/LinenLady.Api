namespace LinenLady.Inventory.Api.Controllers;

using LinenLady.API.Api.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Inventory.Items.Handler;
using LinenLady.API.Inventory.Sql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("items")]
public sealed class InventoryController(
    IInventoryRepository repo,
    GetItemsHandler listHandler,
    UpdateItemHandler updateHandler,
    SoftDeleteItemHandler deleteHandler,
    CreateItemsHandler createHandler) : ControllerBase
{
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
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetItems(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 10,
        [FromQuery] string status = "all",
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        var (result, response) = await listHandler.Handle(new GetItemsQuery
        {
            Page = page,
            Limit = limit,
            Status = status,
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
    [AllowAnonymous]
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest("Invalid id.");

        var item = await repo.GetByKey(ItemKey.ById(id), ct);
        return item is null ? NotFound("Item not found.") : Ok(item);
    }

    // GET /items/sku/{sku}  — public storefront
    [AllowAnonymous]
    [HttpGet("sku/{sku}")]
    public async Task<IActionResult> GetBySku(string sku, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sku)) return BadRequest("Invalid sku.");

        var item = await repo.GetByKey(ItemKey.BySku(sku), ct);
        return item is null ? NotFound("Item not found.") : Ok(item);
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
}
