using LinenLady.API.AI.Client;
using LinenLady.API.AI.Embeddings.Service;
using LinenLady.API.AI.Keywords.Service;
using LinenLady.API.AI.Options;
using LinenLady.API.AI.Prefill.Service;
using LinenLady.API.AI.Rewrite.Service;
using LinenLady.API.AI.Seo.Service;
using LinenLady.API.Api.Filters;
using LinenLady.API.BackgroundServices;
using LinenLady.API.Blob.Options;
using LinenLady.API.Customer.Sql;
using LinenLady.API.Customers.Handler;
using LinenLady.API.Inventory.AiMeta.Sql;
using LinenLady.API.Inventory.Images.Handler;
using LinenLady.API.Inventory.Images.Sql;
using LinenLady.API.Inventory.Items.Handler;
using LinenLady.API.Inventory.Sql;
using LinenLady.API.Search.Handler;
using LinenLady.API.Site.Blob;
using LinenLady.API.Site.Handler;
using LinenLady.API.Site.Sql;
using LinenLady.API.Square;

var builder = WebApplication.CreateBuilder(args);

// ─── Connection string fallback ──────────────────────────────────────────────
// Support both the legacy env-var style (SQL_CONNECTION_STRING) used by the
// old Functions app and the conventional ConnectionStrings:Sql configuration
// slot that all the new repositories read from. If only the env-var form is
// set, promote it into the ConnectionStrings:Sql slot at startup.
var envSqlConn = builder.Configuration["SQL_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(envSqlConn) &&
    string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Sql")))
{
    builder.Configuration["ConnectionStrings:Sql"] = envSqlConn;
}

// ─── Options binding ─────────────────────────────────────────────────────────
builder.Services.Configure<AzureOpenAiOptions>(
    builder.Configuration.GetSection(AzureOpenAiOptions.SectionName));
builder.Services.Configure<BlobStorageOptions>(
    builder.Configuration.GetSection(BlobStorageOptions.SectionName));
builder.Services.Configure<SquareOptions>(
    builder.Configuration.GetSection(SquareOptions.SectionName));

// ─── AI: shared clients ──────────────────────────────────────────────────────
// Both clients receive HttpClient via IHttpClientFactory so retry/timeout
// policies can be layered on later without touching the services themselves.
builder.Services.AddHttpClient<AzureOpenAiChatClient>();
builder.Services.AddHttpClient<AzureOpenAiEmbeddingsClient>();

// ─── AI: features ────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAiPrefillService, AiPrefillService>();
builder.Services.AddScoped<IAiRewriteService, AiRewriteService>();
builder.Services.AddScoped<IAiEmbeddingsService, AiEmbeddingsService>();
builder.Services.AddScoped<IAiKeywordsService, AiKeywordsService>();
builder.Services.AddScoped<IAiSeoService, AiSeoService>();

// ─── Inventory: items ────────────────────────────────────────────────────────
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IInventoryAiMetaRepository, InventoryAiMetaRepository>();
builder.Services.AddScoped<GetItemsHandler>();
builder.Services.AddScoped<CreateItemsHandler>();
builder.Services.AddScoped<UpdateItemHandler>();
builder.Services.AddScoped<SoftDeleteItemHandler>();

// ─── Inventory: images ───────────────────────────────────────────────────────
builder.Services.AddScoped<IInventoryImageRepository, InventoryImageRepository>();
builder.Services.AddScoped<IInventoryImagesQuery, InventoryImagesQuery>();
builder.Services.AddScoped<GetImagesHandler>();
builder.Services.AddScoped<AddImagesHandler>();
builder.Services.AddScoped<NewBlobUrlHandler>();
builder.Services.AddScoped<DeleteImageHandler>();
builder.Services.AddScoped<ReplaceImageHandler>();
builder.Services.AddScoped<SetPrimaryImageHandler>();

// ─── Search ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<SimilarItemsHandler>();

// ─── Customers / reservations / messages ─────────────────────────────────────
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<SyncCustomerHandler>();
builder.Services.AddScoped<GetMyProfileHandler>();
builder.Services.AddScoped<UpdateProfileHandler>();
builder.Services.AddScoped<UpsertAddressHandler>();
builder.Services.AddScoped<DeleteAddressHandler>();
builder.Services.AddScoped<SetPreferencesHandler>();
builder.Services.AddScoped<CreateReservationHandler>();
builder.Services.AddScoped<CancelReservationHandler>();
builder.Services.AddScoped<SquareWebhookHandler>();
builder.Services.AddScoped<ExpireReservationsHandler>();
builder.Services.AddScoped<MessageHandler>();

// Square
builder.Services.AddHttpClient("square");
builder.Services.AddScoped<ISquareService, SquareService>();

// Background job: hourly reservation expiration (replaces the old timer-trigger)
builder.Services.AddHostedService<ExpireReservationsBackgroundService>();

// ─── Site ────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ISiteRepository, SiteRepository>();
builder.Services.AddScoped<SiteMediaSasService>();
builder.Services.AddScoped<SiteMediaHandler>();
builder.Services.AddScoped<SiteConfigHandler>();
builder.Services.AddScoped<SiteHeroHandler>();

// ─── MVC + global exception filter ───────────────────────────────────────────
builder.Services.AddControllers(options =>
{
    options.Filters.Add<DomainExceptionFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ─── Pipeline ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
