namespace LinenLady.API.Controllers;

using LinenLady.API.Api.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Inventory.Images.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/items/{id:int}/images")]
public sealed class ImagesController(
    GetImagesHandler getHandler,
    AddImagesHandler addHandler,
    NewBlobUrlHandler newBlobUrlHandler,
    DeleteImageHandler deleteHandler,
    ReplaceImageHandler replaceHandler,
    SetPrimaryImageHandler setPrimaryHandler,
    IConfiguration configuration) : ControllerBase
{
    // GET /items/{id}/images?ttlMinutes=60  — public storefront needs image URLs
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> GetImages(
        int id,
        [FromQuery] int? ttlMinutes,
        CancellationToken ct)
    {
        if (id <= 0) return BadRequest("Invalid id.");

        var blobConn = configuration.GetConnectionString("BlobStorage")
            ?? configuration["BlobStorage:ConnectionString"];
        var container = configuration["BlobStorage:ImageContainerName"];

        if (string.IsNullOrWhiteSpace(blobConn) || string.IsNullOrWhiteSpace(container))
            return StatusCode(500, "Missing blob storage configuration.");

        var (exists, images) = await getHandler.Handle(id, ttlMinutes, blobConn, container, ct);
        return exists ? Ok(images) : NotFound("Item not found.");
    }

    // POST /items/{id}/images
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPost]
    public async Task<IActionResult> AddImages(
        int id,
        [FromBody] AddImagesRequest? body,
        CancellationToken ct)
    {
        if (body is null) return BadRequest("Invalid JSON body.");

        var result = await addHandler.HandleAsync(id, body, ct);
        return StatusCode(201, result);
    }

    // GET /items/{id}/images/new-blob-url?fileName=x.jpg&contentType=image/jpeg
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpGet("new-blob-url")]
    public async Task<IActionResult> GetNewBlobUrl(
        int id,
        [FromQuery] string? fileName,
        [FromQuery] string? contentType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return BadRequest("fileName query param is required.");

        var info = await newBlobUrlHandler.HandleAsync(
            id,
            fileName,
            contentType ?? "image/jpeg",
            ct);

        return Ok(info);
    }

    // DELETE /items/{id}/images/{imageId}
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpDelete("{imageId:int}")]
    public async Task<IActionResult> DeleteImage(
        int id,
        int imageId,
        CancellationToken ct)
    {
        await deleteHandler.HandleAsync(id, imageId, ct);
        return NoContent();
    }

    // GET /items/{id}/images/{imageId}/replace-url
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpGet("{imageId:int}/replace-url")]
    public async Task<IActionResult> GetReplaceUrl(
        int id,
        int imageId,
        CancellationToken ct)
    {
        var info = await replaceHandler.HandleAsync(id, imageId, ct);
        return Ok(new
        {
            info.UploadUrl,
            info.RequiredHeaders,
            info.ContentType,
            info.BlobName,
        });
    }

    // PATCH /items/{id}/images/{imageId}/primary
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPatch("{imageId:int}/primary")]
    public async Task<IActionResult> SetPrimary(
        int id,
        int imageId,
        CancellationToken ct)
    {
        await setPrimaryHandler.HandleAsync(id, imageId, ct);
        return NoContent();
    }
}
