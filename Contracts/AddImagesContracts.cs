namespace LinenLady.API.Contracts;

public static class AddImagesContracts
{
    public sealed record NewImageRequest(
        string ImagePath,
        bool? IsPrimary,
        int? SortOrder
    );

    public sealed record AddImagesRequest(
        List<NewImageRequest> Images
    );

    public sealed record AddImagesResult(
        int InventoryId,
        List<InventoryImageDto> Images
    );
}
