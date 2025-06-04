using Microsoft.EntityFrameworkCore;
using SmartReceiptOrganizer.Data;
using SmartReceiptOrganizer.Services;
using SmartReceiptOrganizer.Core.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add controllers with JSON configuration
builder.Services.AddControllers();

// Database IReceiptRepository
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ReceiptDbContext>(options =>
{
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
        sqlOptions.CommandTimeout(30);
    });

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

builder.Services.AddScoped<IReceiptRepository, ReceiptRepository>();    
builder.Services.AddScoped<IReceiptParsingService, AdvancedReceiptParsingService>();      

// Business Services
builder.Services.AddScoped<IReceiptProcessingService, ReceiptProcessingService>();
builder.Services.AddScoped<IWebhookLoggingService, WebhookLoggingService>();

// HTTP Client
builder.Services.AddHttpClient();

// CORS für Debugging
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure pipeline
app.UseCors("AllowAll");
app.UseRouting();
app.MapControllers();

// Database initialization (non-blocking)
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(1000);
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ReceiptDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        logger.LogInformation("🗄️ Initializing database...");

        var canConnect = await context.Database.CanConnectAsync();
        if (canConnect)
        {
            logger.LogInformation("✅ Database connection successful");
            await context.Database.EnsureCreatedAsync();
            logger.LogInformation("✅ Database ready");
        }
        else
        {
            logger.LogWarning("⚠️ Cannot connect to database - will create new one");
            await context.Database.EnsureCreatedAsync();
            logger.LogInformation("✅ Database created");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Database initialization failed: {ex.Message}");
    }
});

// Simple startup info
Console.WriteLine("🚀 Smart Receipt Organizer starting...");
Console.WriteLine($"📍 Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"🔗 Webhook URL: {(app.Environment.IsDevelopment() ? "https://localhost:5001" : "https://postmandevto.runasp.net")}/api/postmark/inbound");
Console.WriteLine("📡 Available endpoints:");
Console.WriteLine("   - POST /api/postmark/inbound (Postmark webhook)");
Console.WriteLine("   - GET  /api/webhook-logs (View webhook logs)");
Console.WriteLine("   - POST /api/debug/webhook-raw (Debug webhook)");

app.Run();