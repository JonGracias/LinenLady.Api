namespace LinenLady.API.AI.Embeddings.Service;

using LinenLady.API.Contracts;

public interface IAiEmbeddingsService
{
    Task<RefreshVectorOutcome> RefreshAsync(
        int inventoryId,
        RefreshVectorRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated-ish result so the controller can map to the right HTTP status
/// without catching exceptions for flow control.
/// </summary>
public sealed record RefreshVectorOutcome(
    RefreshVectorStatus Status,
    RefreshVectorResult? Result,
    string? ErrorMessage);

public sealed record RefreshVectorResult(
    int InventoryId,
    string Purpose,
    string Model,
    string ChangeStatus, // "created" | "updated" | "unchanged"
    int Dimensions,
    int VectorId);

public enum RefreshVectorStatus
{
    Ok,
    NotFound,
    InvalidId,
    PurposeTooLong,
    NoTextToEmbed
}
