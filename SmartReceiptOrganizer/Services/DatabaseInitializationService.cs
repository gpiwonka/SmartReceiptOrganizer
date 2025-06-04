using Microsoft.EntityFrameworkCore;
using SmartReceiptOrganizer.Data;

namespace SmartReceiptOrganizer.Services
{
    public class DatabaseInitializationService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DatabaseInitializationService> _logger;

        public DatabaseInitializationService(
            IServiceProvider serviceProvider,
            ILogger<DatabaseInitializationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(1000, cancellationToken); // Warte bis App bereit

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();

                _logger.LogInformation("🗄️  Starting database initialization...");

                var retryCount = 0;
                while (retryCount < 5 && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var canConnect = await context.Database.CanConnectAsync(cancellationToken);
                        if (canConnect)
                        {
                            _logger.LogInformation("✅ Database connection successful");

                            var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
                            if (pendingMigrations.Any())
                            {
                                _logger.LogInformation("📦 Applying {Count} pending migrations", pendingMigrations.Count());
                                await context.Database.MigrateAsync(cancellationToken);
                            }
                            else
                            {
                                _logger.LogInformation("✅ Database is up to date");
                            }
                            break;
                        }
                        else
                        {
                            _logger.LogWarning("⚠️  Cannot connect to database - trying EnsureCreated");
                            await context.Database.EnsureCreatedAsync(cancellationToken);
                            break;
                        }
                    }
                    catch (Exception ex) when (retryCount < 4)
                    {
                        retryCount++;
                        _logger.LogWarning(ex, "⚠️  Database initialization attempt {Retry}/5 failed, retrying in 5 seconds...", retryCount);
                        await Task.Delay(5000, cancellationToken);
                    }
                }

                _logger.LogInformation("✅ Database initialization completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Database initialization failed");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
