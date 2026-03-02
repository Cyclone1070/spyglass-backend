using Microsoft.Extensions.Options;
using MongoDB.Driver;
using spyglass_backend.Configuration;
using spyglass_backend.Features.Links;
using spyglass_backend.Features.WebUtils;
using spyglass_backend.Features.Search;

var builder = WebApplication.CreateBuilder(args);

// Configure logging filter in development environment
// if (builder.Environment.IsDevelopment())
// {
// 	builder.Logging.AddFilter((provider, category, logLevel) =>
// 	{
// 		if (logLevel != LogLevel.Debug && logLevel != LogLevel.Warning)
// 		{
// 			return false;
// 		}
// 		return true;
// 	});
// }

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient(Options.DefaultName, client =>
{
	client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
	AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli,
	ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
});

builder.Services.AddHttpClient("ProxyClient", client =>
{
	client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
	Proxy = new System.Net.WebProxy("http://proxy-spyglass.cyc.fyi"),
	UseProxy = true,
	AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli,
	ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Add scraper rules
builder.Services.Configure<ScraperRules>(builder.Configuration.GetSection("ScraperRules"));

// Add MongoDB configuration
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbSettings"));

// Add search configuration
builder.Services.Configure<SearchSettings>(builder.Configuration.GetSection("SearchSettings"));

builder.Services.AddSingleton<IMongoClient>(sp =>
	new MongoClient(sp.GetRequiredService<IOptions<MongoDbSettings>>().Value.ConnectionString));

builder.Services.AddScoped(sp =>
	sp.GetRequiredService<IMongoClient>().GetDatabase(sp.GetRequiredService<IOptions<MongoDbSettings>>().Value.DatabaseName));

// Add custom services
builder.Services.AddScoped<MongoLinkService>();
builder.Services.AddSingleton<WebsiteLinkService>();
builder.Services.AddSingleton<SearchLinkService>();
builder.Services.AddSingleton<MegathreadService>();
builder.Services.AddScoped<MongoResultService>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddSingleton<WebService>();
builder.Services.AddSingleton<SearchOrchestrationService>();

var app = builder.Build();

// Create TTL index on the 'results' collection
using (var scope = app.Services.CreateScope())
{
	try
	{
		var services = scope.ServiceProvider;
		var logger = services.GetRequiredService<ILogger<Program>>();
		var database = services.GetRequiredService<IMongoDatabase>();
		var resultsCollection = database.GetCollection<StoredResult>("results");
		var indexKeysDefinition = Builders<StoredResult>.IndexKeys.Ascending(x => x.CreatedAt);
		var indexOptions = new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(7) }; // Set the desired expiration time
		var indexModel = new CreateIndexModel<StoredResult>(indexKeysDefinition, indexOptions);
		
		await resultsCollection.Indexes.CreateOneAsync(indexModel);
		logger.LogInformation("TTL index created successfully on results collection with 7 day expiration");
	}
	catch (Exception ex)
	{
		var services = scope.ServiceProvider;
		var logger = services.GetRequiredService<ILogger<Program>>();
		logger.LogError(ex, "Failed to create TTL index on results collection. Application startup aborted.");
		throw; // Fail fast - if we can't create the index, don't start the app
	}
}

// Configure the HTTP request pipeline and cors.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
	// In Development, we modify the existing policy to also allow our local frontend
	app.UseCors(policy => policy.WithOrigins("http://localhost:5173")
								.AllowAnyHeader()
								.AllowAnyMethod());
}
else
{
	// In Production, we use a strict policy for the production frontend.
	app.UseCors(policy => policy.WithOrigins("https://spyglass.cyc.fyi")
								.AllowAnyHeader()
								.AllowAnyMethod());
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
