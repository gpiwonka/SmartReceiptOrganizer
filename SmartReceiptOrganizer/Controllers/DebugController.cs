// Controllers/DebugController.cs - Minimaler Controller für Debugging
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace SmartReceiptOrganizer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly ILogger<DebugController> _logger;

        public DebugController(ILogger<DebugController> logger)
        {
            _logger = logger;
        }

        // Einfachster möglicherweise Webhook - akzeptiert ALLES
        [HttpPost("webhook-raw")]
        public async Task<IActionResult> WebhookRaw()
        {
            try
            {
                // Request komplett lesen ohne Parsing
                Request.EnableBuffering();
                var buffer = new byte[Convert.ToInt32(Request.ContentLength ?? 0)];
                await Request.Body.ReadAsync(buffer, 0, buffer.Length);
                var body = Encoding.UTF8.GetString(buffer);
                Request.Body.Position = 0;

                // Alles sammeln
                var debugInfo = new
                {
                    timestamp = DateTime.UtcNow,
                    method = Request.Method,
                    path = Request.Path.Value,
                    contentType = Request.ContentType,
                    contentLength = Request.ContentLength,
                    headers = @"Request.Headers",
                    queryString = Request.QueryString.Value,
                    bodyLength = body.Length,
                    bodyPreview = body.Length > 1000 ? body.Substring(0, 1000) + "..." : body,
                    userAgent = Request.Headers.UserAgent.ToString(),
                    remoteIp = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
                };

                _logger.LogInformation("RAW WEBHOOK DEBUG: {@DebugInfo}", debugInfo);

                return Ok(new
                {
                    success = true,
                    message = "Raw webhook received successfully",
                    receivedAt = DateTime.UtcNow,
                    debugInfo = debugInfo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in raw webhook");
                return Ok(new
                {
                    success = false,
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // Test ohne Body Parameter
        [HttpPost("webhook-simple")]
        public IActionResult WebhookSimple()
        {
            return Ok(new
            {
                success = true,
                message = "Simple webhook working",
                timestamp = DateTime.UtcNow,
                method = Request.Method,
                contentType = Request.ContentType
            });
        }

        // Test mit Body als String
        [HttpPost("webhook-string")]
        public async Task<IActionResult> WebhookString([FromBody] string body)
        {
            return Ok(new
            {
                success = true,
                receivedBody = body,
                bodyType = body?.GetType().Name,
                timestamp = DateTime.UtcNow
            });
        }

        // Test mit JsonElement (roher JSON)
        [HttpPost("webhook-json")]
        public IActionResult WebhookJson([FromBody] System.Text.Json.JsonElement json)
        {
            try
            {
                return Ok(new
                {
                    success = true,
                    receivedJson = json.ToString(),
                    jsonType = json.ValueKind.ToString(),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        // GET Test um sicherzustellen dass Endpoint erreichbar ist
        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                message = "Debug endpoint is working",
                timestamp = DateTime.UtcNow,
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                baseUrl = $"{Request.Scheme}://{Request.Host}"
            });
        }

        // OPTIONS für CORS
        [HttpOptions("webhook-raw")]
        public IActionResult OptionsWebhookRaw()
        {
            return Ok();
        }
    }
}
