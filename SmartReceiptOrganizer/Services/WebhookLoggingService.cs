using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SmartReceiptOrganizer.Core.Interfaces;
using SmartReceiptOrganizer.Core.Models;
using SmartReceiptOrganizer.Core.Models;
using SmartReceiptOrganizer.Data;
using System.Text.Json;

namespace SmartReceiptOrganizer.Services
{
    public class WebhookLoggingService : IWebhookLoggingService
    {
        private readonly ReceiptDbContext _context;
        private readonly ILogger<WebhookLoggingService> _logger;

        public WebhookLoggingService(ReceiptDbContext context, ILogger<WebhookLoggingService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<WebhookLog> LogWebhookReceivedAsync(HttpRequest request, string source = "Postmark")
        {
            try
            {
                // Request Body lesen
                string requestBody = "";
                if (request.Body.CanSeek)
                {
                    request.Body.Seek(0, SeekOrigin.Begin);
                }

                using (var reader = new StreamReader(request.Body, leaveOpen: true))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                if (request.Body.CanSeek)
                {
                    request.Body.Seek(0, SeekOrigin.Begin);
                }

                // Headers sammeln
                var headers = request.Headers.ToDictionary(
                    h => h.Key,
                    h => String.Join(", ", h.Value.ToArray())
                );

                // Versuche Postmark-spezifische Felder zu extrahieren
                string ? messageId = null;
                string? subject = null;
                string? fromEmail = null;
                string? toEmail = null;
                bool hasAttachments = false;
                int attachmentCount = 0;

                try
                {
                    if (!string.IsNullOrEmpty(requestBody) && requestBody.StartsWith("{"))
                    {
                        var json = JsonDocument.Parse(requestBody);
                        var root = json.RootElement;

                        if (root.TryGetProperty("MessageID", out var msgId))
                            messageId = msgId.GetString();

                        if (root.TryGetProperty("Subject", out var subj))
                            subject = subj.GetString();

                        if (root.TryGetProperty("From", out var from))
                            fromEmail = from.GetString();

                        if (root.TryGetProperty("To", out var to))
                            toEmail = to.GetString();

                        if (root.TryGetProperty("Attachments", out var attachments) &&
                            attachments.ValueKind == JsonValueKind.Array)
                        {
                            attachmentCount = attachments.GetArrayLength();
                            hasAttachments = attachmentCount > 0;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore JSON parsing errors für Log-Zwecke
                }

                var webhookLog = new WebhookLog
                {
                    Source = source,
                    MessageId = messageId,
                    Subject = subject?.Length > 500 ? subject.Substring(0, 500) : subject,
                    FromEmail = fromEmail?.Length > 200 ? fromEmail.Substring(0, 200) : fromEmail,
                    ToEmail = toEmail?.Length > 200 ? toEmail.Substring(0, 200) : toEmail,
                    ContentType = request.ContentType ?? "unknown",
                    ContentLength = requestBody.Length,
                    Status = "Received",
                    RequestHeaders = JsonSerializer.Serialize(headers),
                    RequestBody = requestBody.Length > 50000 ? requestBody.Substring(0, 50000) + "... [TRUNCATED]" : requestBody,
                    ReceivedAt = DateTime.UtcNow,
                    HasAttachments = hasAttachments,
                    AttachmentCount = attachmentCount
                };

                _context.WebhookLogs.Add(webhookLog);
                await _context.SaveChangesAsync();

                return webhookLog;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging webhook to database");

                // Fallback: Minimaler Log-Eintrag
                var fallbackLog = new WebhookLog
                {
                    Source = source,
                    Status = "Failed",
                    ErrorMessage = $"Logging error: {ex.Message}",
                    ReceivedAt = DateTime.UtcNow
                };

                try
                {
                    _context.WebhookLogs.Add(fallbackLog);
                    await _context.SaveChangesAsync();
                    return fallbackLog;
                }
                catch
                {
                    // Complete fallback - return in-memory log
                    return fallbackLog;
                }
            }
        }

        public async Task UpdateWebhookLogAsync(int logId, string status, string? errorMessage = null, object? details = null)
        {
            try
            {
                var log = await _context.WebhookLogs.FindAsync(logId);
                if (log != null)
                {
                    log.Status = status;
                    log.ErrorMessage = errorMessage?.Length > 1000 ? errorMessage.Substring(0, 1000) : errorMessage;

                    if (details != null)
                    {
                        log.ProcessingDetails = JsonSerializer.Serialize(details);
                    }

                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating webhook log {LogId}", logId);
            }
        }

        public async Task<WebhookLog> LogWebhookProcessedAsync(int logId, bool success, int? receiptId = null, object? details = null)
        {
            try
            {
                var log = await _context.WebhookLogs.FindAsync(logId);
                if (log != null)
                {
                    log.Status = success ? "Processed" : "Failed";
                    log.ProcessedAt = DateTime.UtcNow;
                    log.ProcessingTimeMs = (int)(DateTime.UtcNow - log.ReceivedAt).TotalMilliseconds;
                    log.CreatedReceiptId = receiptId;

                    if (details != null)
                    {
                        log.ProcessingDetails = JsonSerializer.Serialize(details);
                    }

                    await _context.SaveChangesAsync();
                }
                return log!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating webhook log completion for {LogId}", logId);
                return null!;
            }
        }

        public async Task<List<WebhookLog>> GetRecentLogsAsync(int count = 50)
        {
            return await _context.WebhookLogs
                .OrderByDescending(l => l.ReceivedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<WebhookLog?> GetLogByIdAsync(int id)
        {
            return await _context.WebhookLogs.FindAsync(id);
        }

        public async Task CleanupOldLogsAsync(int daysToKeep = 30)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
            var oldLogs = await _context.WebhookLogs
                .Where(l => l.ReceivedAt < cutoffDate)
                .ToListAsync();

            if (oldLogs.Any())
            {
                _context.WebhookLogs.RemoveRange(oldLogs);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Cleaned up {Count} old webhook logs", oldLogs.Count);
            }
        }
    }
}