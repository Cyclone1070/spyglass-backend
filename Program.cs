using spyglass_backend.Configuration;
using spyglass_backend.Features.Links;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add scraper rules
builder.Configuration.AddJsonFile("scraperules.json", optional: false, reloadOnChange: true);
builder.Services.Configure<ScraperRules>(builder.Configuration);

// Add custom services
builder.Services.AddSingleton<WebsiteLinkService>();
builder.Services.AddSingleton<SearchLinkService>();
builder.Services.AddSingleton<ResultCardSelectorService>();
builder.Services.AddSingleton<MegathreadService>();
builder.Services.AddSingleton<LinkExportService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
