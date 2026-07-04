// Features/Orders/Handler/AdminOrdersHandler.cs
//
// Back-office orders: list, detail (with item thumbnails), and fulfillment
// checkpoint updates. Thumbnails reuse the same primary-image lookup +
// public-URL pattern as the order emails (SquareWebhookHandler).

namespace LinenLady.API.Features.Orders.Handler;

using LinenLady.API.Blob;
using LinenLady.API.Blob.Options;
using LinenLady.API.Contracts;
using LinenLady.API.Customers.Sql;
using LinenLady.API.Inventory.Images.Sql;
using Microsoft.Extensions.Options;

public sealed class AdminOrdersHandler(
    ICustomerRepository          repo,
    IInventoryImagesQuery        imagesQuery,
    IOptions<BlobStorageOptions> blobOptions,
    ILogger<AdminOrdersHandler>  log)
{
    private readonly BlobStorageOptions _blob = blobOptions.Value;

    public Task<List<AdminOrderListItem>> GetAll() => repo.GetAllOrdersAdminAsync();

    public async Task<AdminOrderDetail?> GetById(int orderId, CancellationToken ct)
    {
        var detail = await repo.GetOrderAdminAsync(orderId);
        if (detail is null) return null;

        return detail with { Order = await WithThumbnailsAsync(detail.Order, ct) };
    }

    public async Task<(bool Ok, string? Error, AdminOrderDetail? Detail)> SetCheckpoint(
        int orderId, SetOrderCheckpointRequest req, CancellationToken ct)
    {
        var existing = await repo.GetOrderAdminAsync(orderId);
        if (existing is null)
            return (false, "Order not found.", null);

        // Fulfillment only makes sense once money has landed. Cancelled /
        // pending / failed orders keep their payment status as the story.
        if (!string.Equals(existing.Order.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            return (false, $"Only paid orders can be updated (this order is {existing.Order.Status}).", null);

        try
        {
            await repo.SetOrderCheckpointAsync(orderId, req.Checkpoint, req.Clear);
        }
        catch (ArgumentException ex)
        {
            return (false, ex.Message, null);
        }

        return (true, null, await GetById(orderId, ct));
    }

    /// <summary>
    /// Attach each item's primary photo as a stable public blob URL.
    /// Best-effort — thumbnails are decorative, so any failure returns the
    /// order untouched rather than failing the request.
    /// </summary>
    private async Task<OrderDto> WithThumbnailsAsync(OrderDto order, CancellationToken ct)
    {
        try
        {
            var ids   = order.Items.Select(i => i.InventoryId).Distinct().ToList();
            var paths = await imagesQuery.GetPrimaryImagePaths(ids, ct);

            var items = order.Items
                .Select(i => paths.TryGetValue(i.InventoryId, out var path)
                    ? i with
                      {
                          ThumbnailUrl = BlobSas.BuildPublicUrl(
                              _blob.ConnectionString,
                              _blob.ImageContainerName,
                              path.TrimStart('/'))
                      }
                    : i)
                .ToList();

            return order with { Items = items };
        }
        catch (Exception ex)
        {
            log.LogWarning(ex,
                "Thumbnail lookup failed for admin order {OrderId} (non-fatal).", order.OrderId);
            return order;
        }
    }
}
