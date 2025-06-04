// Controllers/DatabaseDebugController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartReceiptOrganizer.Core.Models;
using SmartReceiptOrganizer.Data;

namespace SmartReceiptOrganizer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseDebugController : ControllerBase
    {
        private readonly ReceiptDbContext _context;
        private readonly ILogger<DatabaseDebugController> _logger;
        private readonly IConfiguration _configuration;

        public DatabaseDebugController(
            ReceiptDbContext context,
            ILogger<DatabaseDebugController> logger,
            IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet("connection-test")]
        public async Task<IActionResult> TestDatabaseConnection()
        {
            try
            {
                _logger.LogInformation("Testing database connection...");

                // 1. Connection String prüfen
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                // 2. Kann Database erreicht werden?
                var canConnect = await _context.Database.CanConnectAsync();

                // 3. Pending Migrations?
                var pendingMigrations = await _context.Database.GetPendingMigrationsAsync();

                // 4. Applied Migrations?
                var appliedMigrations = await _context.Database.GetAppliedMigrationsAsync();

                // 5. Einfache Query
                var receiptCount = 0;
                try
                {
                    receiptCount = await _context.Receipts.CountAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not count receipts - table might not exist");
                }

                return Ok(new
                {
                    success = canConnect,
                    connectionString = connectionString?.Substring(0, Math.Min(50, connectionString.Length)) + "...",
                    canConnect = canConnect,
                    receiptCount = receiptCount,
                    appliedMigrations = appliedMigrations.ToList(),
                    pendingMigrations = pendingMigrations.ToList(),
                    databaseProvider = _context.Database.ProviderName,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection test failed");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    innerException = ex.InnerException?.Message,
                    stackTrace = ex.StackTrace?.Split('\n').Take(5).ToArray()
                });
            }
        }

        [HttpPost("ensure-created")]
        public async Task<IActionResult> EnsureDatabaseCreated()
        {
            try
            {
                _logger.LogInformation("Ensuring database is created...");

                var created = await _context.Database.EnsureCreatedAsync();

                return Ok(new
                {
                    success = true,
                    databaseCreated = created,
                    message = created ? "Database was created" : "Database already existed",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure database creation");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    innerException = ex.InnerException?.Message
                });
            }
        }

        [HttpPost("migrate")]
        public async Task<IActionResult> MigrateDatabase()
        {
            try
            {
                _logger.LogInformation("Running database migrations...");

                await _context.Database.MigrateAsync();

                return Ok(new
                {
                    success = true,
                    message = "Database migrations completed",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database migration failed");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    innerException = ex.InnerException?.Message
                });
            }
        }

        [HttpPost("test-insert")]
        public async Task<IActionResult> TestInsert()
        {
            try
            {
                var testReceipt = new Receipt
                {
                    EmailId = $"test-{DateTime.UtcNow.Ticks}",
                    Merchant = "Test Store",
                    Amount = 99.99m,
                    Currency = "EUR",
                    TransactionDate = DateTime.UtcNow,
                    ReceivedDate = DateTime.UtcNow,
                    Category = "Test",
                    OriginalEmailSubject = "Database Test Receipt",
                    OriginalEmailBody = "This is a test receipt to verify database functionality"
                };

                _context.Receipts.Add(testReceipt);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Test receipt inserted successfully",
                    receiptId = testReceipt.Id,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test insert failed");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    innerException = ex.InnerException?.Message
                });
            }
        }

        [HttpGet("receipts")]
        public async Task<IActionResult> GetReceipts()
        {
            try
            {
                var receipts = await _context.Receipts
                    .OrderByDescending(r => r.ReceivedDate)
                    .Take(10)
                    .Select(r => new
                    {
                        r.Id,
                        r.EmailId,
                        r.Merchant,
                        r.Amount,
                        r.Currency,
                        r.TransactionDate,
                        r.ReceivedDate,
                        r.Category,
                        r.OriginalEmailSubject
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    count = receipts.Count,
                    receipts = receipts
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get receipts");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    innerException = ex.InnerException?.Message
                });
            }
        }

        [HttpGet("environment")]
        public IActionResult GetEnvironmentInfo()
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");

                return Ok(new
                {
                    environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                    connectionStringExists = !string.IsNullOrEmpty(connectionString),
                    connectionStringPreview = connectionString?.Substring(0, Math.Min(100, connectionString.Length)) + "...",
                    currentDirectory = Directory.GetCurrentDirectory(),
                    machineName = Environment.MachineName,
                    osVersion = Environment.OSVersion.ToString(),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }
    }
}

