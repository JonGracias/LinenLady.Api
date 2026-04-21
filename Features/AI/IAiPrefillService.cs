using LinenLady.Inventory.Api.Contracts;
using LinenLady.Inventory.Api.Contracts.Requests;
using LinenLady.Inventory.Functions.Contracts;

namespace LinenLady.API.AI.Service;

public interface IAiPrefillService
{
    Task<PrefillOutcome> PrefillAsync(
        int inventoryId,
        PrefillMode mode,
        AiPrefillRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated-ish result so the controller can map to the right HTTP status
/// without catching exceptions for flow control.
/// </summary>
public sealed record PrefillOutcome(
    PrefillStatus Status,
    InventoryItemDto? Item,
    string? ErrorMessage);

public enum PrefillStatus
{
    Ok,
    NotFound,
    NoImages,
    NoValidImages,
    InvalidBlobPaths
}
