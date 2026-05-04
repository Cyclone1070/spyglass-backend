namespace spyglass_backend.Features.Links
{
    public class ScraperBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ScraperBackgroundService> _logger;

        public ScraperBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<ScraperBackgroundService> logger
        )
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public static DateTime CalculateNextRunTime(DateTime now)
        {
            // Calculate next Sunday at 00:00:00
            int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7;

            // If it is already Sunday and after midnight, we want the NEXT Sunday
            if (daysUntilSunday == 0 && (now.Hour > 0 || now.Minute > 0 || now.Second > 0))
            {
                daysUntilSunday = 7;
            }

            var nextRun = now.Date.AddDays(daysUntilSunday);
            return nextRun;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Scraper Background Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var nextRun = CalculateNextRunTime(now);
                var delay = nextRun - now;

                if (delay.TotalMilliseconds <= 0)
                {
                    delay = TimeSpan.FromSeconds(1); // Small safety buffer
                }

                _logger.LogInformation(
                    "Next scrape scheduled for {NextRun} (in {Delay} hours)",
                    nextRun,
                    delay.TotalHours
                );

                try
                {
                    await Task.Delay(delay, stoppingToken);

                    _logger.LogInformation("Starting scheduled scrape at {Time}", DateTime.Now);

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var mongoLinkService =
                            scope.ServiceProvider.GetRequiredService<MongoLinkService>();
                        var megathreadService =
                            scope.ServiceProvider.GetRequiredService<MegathreadService>();

                        await mongoLinkService.RemoveAllAsync();
                        var allLinks = await megathreadService.ScrapeMegathreadAsync();
                        await mongoLinkService.CreateManyAsync(allLinks);

                        _logger.LogInformation(
                            "Successfully scraped and saved {Count} links.",
                            allLinks.Count
                        );
                    }
                }
                catch (TaskCanceledException)
                {
                    // Normal shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during scheduled scrape.");
                }
            }
        }
    }
}
