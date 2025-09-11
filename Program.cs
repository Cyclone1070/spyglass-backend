using Microsoft.Extensions.Options;
using MongoDB.Driver;
using spyglass_backend.Configuration;
using spyglass_backend.Features.Links;
using spyglass_backend.Features.Search;

var builder = WebApplication.CreateBuilder(args);

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

// Configure logging filter in development environment
if (builder.Environment.IsDevelopment())
{
	builder.Logging.AddFilter((provider, category, logLevel) =>
	{
		if (logLevel != LogLevel.Debug && logLevel != LogLevel.Warning)
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
	client.Timeout = TimeSpan.FromSeconds(10);
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
// Cross origin Resource Sharing (CORS) policy
builder.Services.AddCors(options =>
{
	options.AddPolicy(name: MyAllowSpecificOrigins,
					  policy =>
					  {
						  // Allow Vite development server
						  policy.WithOrigins("http://localhost:5173")
								.AllowAnyHeader()
								.AllowAnyMethod();
					  });
});

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
builder.Services.AddScoped<MongoResultService>();
builder.Services.AddSingleton<SearchService>();

var app = builder.Build();

// Create TTL index on the 'results' collection
using (var scope = app.Services.CreateScope())
{
	var services = scope.ServiceProvider;
	var database = services.GetRequiredService<IMongoDatabase>();
	var resultsCollection = database.GetCollection<StoredResult>("results");
	var indexKeysDefinition = Builders<StoredResult>.IndexKeys.Ascending(x => x.CreatedAt);
	var indexOptions = new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(7) }; // Set the desired expiration time
	var indexModel = new CreateIndexModel<StoredResult>(indexKeysDefinition, indexOptions);
	resultsCollection.Indexes.CreateOne(indexModel);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.UseCors(MyAllowSpecificOrigins);
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
