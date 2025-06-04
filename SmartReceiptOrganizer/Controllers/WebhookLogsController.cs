using Microsoft.AspNetCore.Mvc;
using SmartReceiptOrganizer.Core.Interfaces;
using SmartReceiptOrganizer.Services;

namespace SmartReceiptOrganizer.Controllers
{
    [ApiController]
    [Route("api/webhook-logs")]
    public class WebhookLogsController : ControllerBase
    {
        private readonly IWebhookLoggingService _webhookLoggingService;

        public WebhookLogsController(IWebhookLoggingService webhookLoggingService)
        {
            _webhookLoggingService = webhookLoggingService;
        }

        [HttpGet]
        public async Task<IActionResult> GetRecentLogs([FromQuery] int count = 20)
        {
            var logs = await _webhookLoggingService.GetRecentLogsAsync(count);

            var summary = logs.Select(l => new
            {
                l.Id,
                l.Source,
                l.MessageId,
                l.Subject,
                l.FromEmail,
                l.Status,
                l.ReceivedAt,
                l.ProcessedAt,
                l.ProcessingTimeMs,
                l.ErrorMessage,
                l.HasAttachments,
                l.AttachmentCount,
                l.CreatedReceiptId,
                l.ContentLength
            });

            return Ok(new
            {
                success = true,
                count = logs.Count,
                logs = summary
            });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetLogDetails(int id)
        {
            var log = await _webhookLoggingService.GetLogByIdAsync(id);
            if (log == null)
            {
                return NotFound();
            }

            return Ok(new
            {
                success = true,
                log = new
                {
                    log.Id,
                    log.Source,
                    log.MessageId,
                    log.Subject,
                    log.FromEmail,
                    log.ToEmail,
                    log.Status,
                    log.ErrorMessage,
                    log.ReceivedAt,
                    log.ProcessedAt,
                    log.ProcessingTimeMs,
                    log.ContentType,
                    log.ContentLength,
                    log.HasAttachments,
                    log.AttachmentCount,
                    log.CreatedReceiptId,
                    RequestHeaders = log.RequestHeaders,
                    RequestBody = log.RequestBody,
                    ProcessingDetails = log.ProcessingDetails
                }
            });
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetLogStats()
        {
            var logs = await _webhookLoggingService.GetRecentLogsAsync(100);

            var stats = new
            {
                total = logs.Count,
                successful = logs.Count(l => l.Status == "Processed"),
                failed = logs.Count(l => l.Status == "Failed"),
                processing = logs.Count(l => l.Status == "Received" || l.Status == "Parsing_Success"),
                avgProcessingTime = logs.Where(l => l.ProcessingTimeMs > 0).Any()
                    ? logs.Where(l => l.ProcessingTimeMs > 0).Average(l => l.ProcessingTimeMs)
                    : 0,
                recentErrors = logs.Where(l => !string.IsNullOrEmpty(l.ErrorMessage))
                    .Take(5)
                    .Select(l => new { l.Id, l.MessageId, l.ErrorMessage, l.ReceivedAt })
                    .ToList(),
                lastSuccessful = logs.Where(l => l.Status == "Processed").FirstOrDefault()?.ReceivedAt,
                withAttachments = logs.Count(l => l.HasAttachments)
            };

            return Ok(new { success = true, stats });
        }

        [HttpDelete("cleanup")]
        public async Task<IActionResult> CleanupOldLogs([FromQuery] int daysToKeep = 30)
        {
            await _webhookLoggingService.CleanupOldLogsAsync(daysToKeep);
            return Ok(new { success = true, message = $"Cleaned up logs older than {daysToKeep} days" });
        }
    }
}