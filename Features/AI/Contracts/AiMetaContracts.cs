namespace LinenLady.API.Contracts;

public sealed class GenerateKeywordsRequest
{
    public string? Hint { get; set; }
}

public sealed class UpsertAdminNotesRequest
{
    public string? AdminNotes { get; set; }
}
