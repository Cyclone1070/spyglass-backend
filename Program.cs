using Microsoft.Extensions.Options;
using MongoDB.Driver;
using spyglass_backend.Configuration;
using spyglass_backend.Features.Links;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
	builder.Logging.AddFilter((provider, category, logLevel) =>
	{
		if (logLevel != LogLevel.Debug)
		{
			return false;
		}
		return true;
	});
}

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient(Options.DefaultName, client =>
{
	client.Timeout = TimeSpan.FromSeconds(15);
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add scraper rules
builder.Services.Configure<ScraperRules>(builder.Configuration.GetSection("ScraperRules"));

// Add MongoDB configuration
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbSettings"));

builder.Services.AddSingleton<IMongoClient>(sp =>
	new MongoClient(sp.GetRequiredService<IOptions<MongoDbSettings>>().Value.ConnectionString));

builder.Services.AddScoped(sp =>
	sp.GetRequiredService<IMongoClient>().GetDatabase(sp.GetRequiredService<IOptions<MongoDbSettings>>().Value.DatabaseName));

// Add custom services
builder.Services.AddScoped<MongoLinkService>();
builder.Services.AddSingleton<WebsiteLinkService>();
builder.Services.AddSingleton<SearchLinkService>();
builder.Services.AddSingleton<ResultCardSelectorService>();
builder.Services.AddSingleton<MegathreadService>();

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
