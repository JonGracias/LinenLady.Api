using LinenLady.API.AI.Client;
using LinenLady.API.AI.Embeddings.Service;
using LinenLady.API.AI.Keywords.Service;
using LinenLady.API.AI.Options;
using LinenLady.API.AI.Prefill.Service;
using LinenLady.API.AI.Rewrite.Service;
using LinenLady.API.AI.Seo.Service;
using LinenLady.API.Auth;
using LinenLady.API.Filters;
using LinenLady.API.BackgroundServices;
using LinenLady.API.Blob.Options;
using LinenLady.API.Customers.Sql;
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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using LinenLady.API.Contracts;
using LinenLady.API.Customers.Services;
using LinenLady.API.Features.Contact;
using LinenLady.API.Features.Contact.Email;
using LinenLady.API.Features.Contact.Service;
using LinenLady.API.Features.Contact.Sql;
using Microsoft.Extensions.Options;
using LinenLady.API.Inventory.Availability.Handler;

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
builder.Services.Configure<ClerkAuthOptions>(
    builder.Configuration.GetSection(ClerkAuthOptions.SectionName));
builder.Services
    .AddOptions<ContactOptions>()
    .Bind(builder.Configuration.GetSection(ContactOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.RecipientEmail), "Contact:RecipientEmail is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.SenderEmail),    "Contact:SenderEmail is required")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ResendApiKey),   "Contact:ResendApiKey is required")
    .ValidateOnStart();

// ── Resend HTTP client ─────────────────────────────────────────────────────
// Single named client; the Authorization header is set per-instance from
// IOptions so a config rotation takes effect without an app restart.
builder.Services.AddHttpClient("resend", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<ContactOptions>>().Value;
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ResendApiKey);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ─── Authentication (Clerk JWT) ──────────────────────────────────────────────
// Clerk's JWKS is discovered via OIDC metadata at {Authority}/.well-known/openid-configuration.
// The handler caches keys and rotates on schedule, so the API stays correct across
// Clerk key rotations without a redeploy. If Authority is unset we fail fast at
// startup — running unauthenticated in any environment is a configuration bug.
var clerkOpts = builder.Configuration.GetSection(ClerkAuthOptions.SectionName)
                                     .Get<ClerkAuthOptions>()
                ?? throw new InvalidOperationException(
                    "Missing configuration section 'Clerk'.");

if (string.IsNullOrWhiteSpace(clerkOpts.Authority))
    throw new InvalidOperationException(
        "Missing configuration 'Clerk:Authority' (Clerk instance URL).");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = clerkOpts.Authority;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = clerkOpts.Authority,
            ValidateAudience         = false, // Clerk doesn't emit aud by default
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
            NameClaimType            = "sub",
        };
    });

// ─── Authorization ───────────────────────────────────────────────────────────
// Two policies:
//   Customer — any authenticated Clerk user. Customer-owned data endpoints.
//   Admin    — authenticated user whose JWT carries an org_id claim matching
//              the configured admin org. Inventory/site/AI mutation endpoints.
//
// The default policy is "must be authenticated" — any controller without an
// explicit [AllowAnonymous] still gets the JWT check.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.Customer, p => p.RequireAuthenticatedUser());

    options.AddPolicy(AuthPolicies.Admin, p => p
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            !string.IsNullOrWhiteSpace(clerkOpts.AdminOrgId)
            && string.Equals(
                ctx.User.GetOrgId(),
                clerkOpts.AdminOrgId,
                StringComparison.Ordinal)));

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
// ─── Frontend Options ──────────────────────────────────────────────────────
builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection("Frontend"));

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

// ─── Inventory: inventory ────────────────────────────────────────────────────────
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IInventoryAiMetaRepository, InventoryAiMetaRepository>();
builder.Services.AddScoped<GetItemsHandler>();
builder.Services.AddScoped<CreateItemsHandler>();
builder.Services.AddScoped<UpdateItemHandler>();
builder.Services.AddScoped<SoftDeleteItemHandler>();
builder.Services.AddScoped<GetAvailabilityHandler>();

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
builder.Services.AddScoped<SquareWebhookHandler>();
builder.Services.AddScoped<ExpireReservationsHandler>();
builder.Services.AddScoped<MessageHandler>();
builder.Services.AddScoped<GetConversationsHandler>();
builder.Services.AddScoped<GetConversationThreadHandler>();
builder.Services.AddScoped<AdminSendMessageHandler>();
builder.Services.AddScoped<GetTotalUnreadInboundHandler>();
builder.Services.AddScoped<GetBasketHandler>();
builder.Services.AddScoped<AddToBasketHandler>();
builder.Services.AddScoped<RemoveFromBasketHandler>();
builder.Services.AddScoped<ReAddToBasketHandler>();
builder.Services.AddScoped<CheckoutHandler>();
builder.Services.AddScoped<GetMyOrdersHandler>();
builder.Services.AddScoped<GetOrderByIdHandler>();
builder.Services.AddScoped<AskNoemiHandler>();
builder.Services.AddScoped<ExpireStaleOrdersHandler>();

// ── Customer Email services ─────────────────────────────────────────
builder.Services.AddScoped<IContactRepository, ContactRepository>();
builder.Services.AddScoped<IEmailSender,       ResendEmailSender>();
builder.Services.AddScoped<IContactService,    ContactService>();

// Square
builder.Services.AddHttpClient("square");
builder.Services.AddScoped<ISquareService, SquareService>();

// Background job: hourly reservation expiration (replaces the old timer-trigger)
builder.Services.AddHostedService<ExpireReservationsBackgroundService>();
builder.Services.AddHostedService<ExpireStaleOrdersBackgroundService>();

// ─── Site ────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ISiteRepository, SiteRepository>();
builder.Services.AddScoped<SiteMediaSasService>();
builder.Services.AddScoped<SiteMediaHandler>();
builder.Services.AddScoped<SiteConfigHandler>();
builder.Services.AddScoped<SiteHeroHandler>();

// ─── MVC + global exception filter ───────────────────────────────────────────
// PropertyNamingPolicy = null preserves DTO property names as-declared
// (PascalCase). Frontend TypeScript types use PascalCase throughout, and
// changing every type annotation and property access on the frontend would be
// a much larger refactor than pinning the JSON naming policy here.
/* builder.Services.AddControllers(options =>
{options.Filters.Add<DomainExceptionFilter>();}).AddJsonOptions(opt =>
    {
        opt.JsonSerializerOptions.PropertyNamingPolicy = null;
    }
); */
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
