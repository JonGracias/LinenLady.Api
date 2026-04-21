using LinenLady.API.Inventory.Images.Sql;
using LinenLady.API.Inventory.Sql;
using LinenLady.API.Site.Media.Sql;
using LinenLady.API.Customer.Sql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var sqlConnStr = builder.Configuration["SQL_CONNECTION_STRING"]
    ?? throw new InvalidOperationException("Missing SQL_CONNECTION_STRING");
var aoaiEndpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"]
    ?? throw new InvalidOperationException("Missing AZURE_OPENAI_ENDPOINT");
var aoaiKey = builder.Configuration["AZURE_OPENAI_KEY"]
    ?? throw new InvalidOperationException("Missing AZURE_OPENAI_API_KEY");
var aoaiDeployment = builder.Configuration["AZURE_OPENAI_DEPLOYMENT"]
    ?? throw new InvalidOperationException("Missing AZURE_OPENAI_DEPLOYMENT");
var aoaiVersion = builder.Configuration["AZURE_OPENAI_API_VERSION"]
    ?? throw new InvalidOperationException("Missing AZURE_OPENAI_VERSION");

// Infrastructure
builder.Services.AddScoped<IInventoryRepository>(_ => new InventoryRepository(sqlConnStr));
builder.Services.AddScoped<IInventoryImageRepository>(_ => new InventoryImageRepository(sqlConnStr));
builder.Services.AddScoped<IInventoryImagesQuery>(_ => new InventoryImagesQuery(sqlConnStr));
builder.Services.AddScoped<ISiteRepository>(_ => new SiteRepository(sqlConnStr));

// Application - Items
builder.Services.AddScoped<CreateItemsHandler>();
builder.Services.AddScoped<UpdateItemHandler>();
builder.Services.AddScoped<SoftDeleteItemHandler>();
builder.Services.AddSingleton<IAiRewriteService>(_ => new AiRewriteService(aoaiEndpoint, aoaiKey, aoaiDeployment, aoaiVersion));

// Application - Images
builder.Services.AddScoped<AddImagesHandler>();
builder.Services.AddScoped<GetImagesHandler>();
builder.Services.AddScoped<SetPrimaryImageHandler>();
builder.Services.AddScoped<ReplaceImageHandler>();
builder.Services.AddScoped<NewBlobUrlHandler>();
builder.Services.AddScoped<DeleteImageHandler>();

// Application - Keywords / Search
builder.Services.AddScoped<GenerateKeywordsHandler>();
builder.Services.AddScoped<GenerateSeoHandler>();
builder.Services.AddScoped<SimilarItemsHandler>();

// Application - Customers
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

// Application - Site
builder.Services.AddSingleton<SiteMediaService>();
builder.Services.AddScoped<ListSiteMediaHandler>();
builder.Services.AddScoped<CreateSiteMediaHandler>();
builder.Services.AddScoped<DeleteSiteMediaHandler>();
builder.Services.AddScoped<ListSiteConfigHandler>();
builder.Services.AddScoped<GetSiteConfigHandler>();
builder.Services.AddScoped<SetSiteConfigHandler>();
builder.Services.AddScoped<ListHeroSlidesHandler>();
builder.Services.AddScoped<CreateHeroSlideHandler>();
builder.Services.AddScoped<UpdateHeroSlideHandler>();
builder.Services.AddScoped<DeleteHeroSlideHandler>();
builder.Services.AddScoped<ReorderHeroSlidesHandler>();

// Square
builder.Services.AddHttpClient("square");
builder.Services.AddScoped<ISquareService, SquareService>();











builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
