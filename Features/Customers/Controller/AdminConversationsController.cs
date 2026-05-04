namespace LinenLady.API.Controllers;

using LinenLady.API.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Customers.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Admin-side messaging endpoints. The Admin policy requires the caller's
/// Clerk JWT to carry an org_id claim matching Clerk:AdminOrgId — same gate
/// the inventory/site mutation endpoints use, so this rides on existing auth
/// without any new policy machinery.
///
/// Routes are scoped under /api/admin/conversations rather than mixed into
/// the existing /api/customers/* tree so the proxy layer and frontend admin
/// directory map 1:1 to the route.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.Admin)]
[Route("api/admin/conversations")]
public sealed class AdminConversationsController(
    GetConversationsHandler        listHandler,
    GetConversationThreadHandler   threadHandler,
    AdminSendMessageHandler        sendHandler,
    GetTotalUnreadInboundHandler   unreadHandler) : ControllerBase
{
    // GET /api/admin/conversations
    // Inbox list — one row per customer with a latest message + unread count.
    [HttpGet]
    public async Task<IActionResult> GetConversations(
        [FromQuery] int? take,
        CancellationToken ct)
    {
        var n = take is > 0 and <= 500 ? take.Value : 100;
        var result = await listHandler.HandleAsync(n, ct);
        return Ok(result);
    }

    // GET /api/admin/conversations/unread-count
    // Drives the admin-nav badge. No side effects.
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
        => Ok(await unreadHandler.HandleAsync(ct));

    // GET /api/admin/conversations/{customerId}
    // Full message thread for one customer. Defaults to marking inbound
    // (customer-from) messages read so the admin's badge clears when she
    // opens the thread; pass ?markRead=false to peek without acknowledging.
    [HttpGet("{customerId:int}")]
    public async Task<IActionResult> GetThread(
        int customerId,
        [FromQuery] bool markRead = true,
        CancellationToken ct = default)
    {
        var result = await threadHandler.HandleAsync(customerId, markRead, ct);
        return Ok(result);
    }

    // POST /api/admin/conversations/{customerId}/messages
    // Admin reply — written as an Outbound message on the customer's thread.
    [HttpPost("{customerId:int}/messages")]
    public async Task<IActionResult> Reply(
        int customerId,
        [FromBody] AdminSendMessageRequest? body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Body))
            return BadRequest("Message body is required.");

        var result = await sendHandler.HandleAsync(customerId, body, ct);
        return StatusCode(201, result);
    }
}
