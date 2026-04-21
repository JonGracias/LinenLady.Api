// Application/Items/UpdateItemHandler.cs
using LinenLady.API.Contracts;
using LinenLady.API.Inventory.Images.Sql;
using LinenLady.API.Inventory.Sql;
using LinenLady.API.AI.Rewrite.Service;

namespace LinenLady.API.Inventory.Items.Handler;

public sealed class UpdateItemHandler
{
    private readonly IInventoryRepository _repo;
    private readonly IInventoryImageRepository _imageRepo;
    private readonly IAiRewriteService _aiService;

    public UpdateItemHandler(
        IInventoryRepository repo,
        IInventoryImageRepository imageRepo,
        IAiRewriteService aiService)
    {
        _repo      = repo;
        _imageRepo = imageRepo;
        _aiService = aiService;
    }

    public async Task<(UpdateItemResult Result, UpdateItemResponse? Response)> Handle(
        int inventoryId,
        UpdateItemRequest request,
        CancellationToken ct)
    {
        // 1. Load current state — single query covers both existence check and field values
        var current = await _repo.GetById(inventoryId, ct);
        if (current is null)
            return (UpdateItemResult.NotFound, null);

        // 2. Resolve updated field values (null = keep current)
        var name       = request.Name        ?? current.Name;
        var description = request.Description ?? current.Description;
        var priceCents  = request.UnitPriceCents ?? current.UnitPriceCents;
        var quantity    = request.QuantityOnHand ?? current.QuantityOnHand;
        var isActive    = request.IsActive    ?? current.IsActive;
        var isFeatured  = request.IsFeatured  ?? current.IsFeatured;

        // Publishing clears the draft flag
        var isDraft = current.IsDraft;
        if (request.IsActive == true)
            isDraft = false;

        // 3. Publish gate — only runs when the request is explicitly activating the item
        //    and the item is not already active (i.e. this is a publish action, not a field edit).
        if (request.IsActive == true && !current.IsActive)
        {
            // Name must be set and not the default placeholder
            if (string.IsNullOrWhiteSpace(name) || name.Equals("Draft", StringComparison.OrdinalIgnoreCase))
                return (UpdateItemResult.BadRequest, null);

            // Price must be set
            if (priceCents <= 0)
                return (UpdateItemResult.BadRequest, null);

            // Must have at least one image
            var imageCount = await _imageRepo.GetImageCount(inventoryId, ct);
            if (imageCount == 0)
                return (UpdateItemResult.BadRequest, null);
        }

        // 4. AI rewrite (if requested)
        if (request.Ai is not null && request.Ai.Fields.Count > 0)
        {
            var aiResult = await _aiService.Rewrite(new AiRewriteInput
            {
                CurrentName        = name,
                CurrentDescription = description ?? "",
                CurrentPriceCents  = priceCents,
                Hint               = request.Ai.Hint ?? "",
                Fields             = request.Ai.Fields
            }, ct);

            if (aiResult is not null)
            {
                if (request.Ai.Fields.Contains("name") && aiResult.Name is not null)
                    name = aiResult.Name;

                if (request.Ai.Fields.Contains("description") && aiResult.Description is not null)
                    description = aiResult.Description;

                if (request.Ai.Fields.Contains("price") && aiResult.UnitPriceCents is not null)
                    priceCents = aiResult.UnitPriceCents.Value;
            }
        }

        // 5. Persist
        var updated = await _repo.Update(inventoryId, new UpdateItemRequest
        {
            Name           = name,
            Description    = description,
            UnitPriceCents = priceCents,
            QuantityOnHand = quantity,
            IsActive       = isActive,
            IsDraft        = isDraft,
            IsFeatured     = isFeatured,
        }, ct);

        if (!updated)
            return (UpdateItemResult.Failed, null);

        // 6. Update primary image if requested (non-fatal)
        if (request.PrimaryImageId.HasValue)
        {
            try
            {
                await _imageRepo.SetPrimaryImage(inventoryId, request.PrimaryImageId.Value, ct);
            }
            catch
            {
                // Non-fatal — the rest of the update succeeded
            }
        }

        // 7. Return updated state
        return (UpdateItemResult.Updated, new UpdateItemResponse
        {
            InventoryId    = inventoryId,
            Name           = name,
            Description    = description,
            UnitPriceCents = priceCents,
            QuantityOnHand = quantity,
            IsActive       = isActive,
            IsDraft        = isDraft,
            IsFeatured     = isFeatured,
            UpdatedAt      = DateTime.UtcNow
        });
    }
}