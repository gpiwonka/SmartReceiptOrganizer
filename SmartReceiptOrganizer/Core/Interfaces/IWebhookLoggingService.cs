using SmartReceiptOrganizer.Core.Models;

namespace SmartReceiptOrganizer.Core.Interfaces
{
    public interface IWebhookLoggingService
    {
        Task<WebhookLog> LogWebhookReceivedAsync(HttpRequest request, string source = "Postmark");
        Task UpdateWebhookLogAsync(int logId, string status, string? errorMessage = null, object? details = null);
        Task<WebhookLog> LogWebhookProcessedAsync(int logId, bool success, int? receiptId = null, object? details = null);
        Task<List<WebhookLog>> GetRecentLogsAsync(int count = 50);
        Task<WebhookLog?> GetLogByIdAsync(int id);
        Task CleanupOldLogsAsync(int daysToKeep = 30);
    }
}
