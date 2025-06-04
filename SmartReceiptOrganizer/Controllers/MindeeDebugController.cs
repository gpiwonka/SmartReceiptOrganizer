// Controllers/MindeeDebugController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartReceiptOrganizer.Core.Models;
using SmartReceiptOrganizer.Data;
using System.Text.Json;

namespace SmartReceiptOrganizer.Controllers
{
    [ApiController]
    [Route("api/mindee-debug")]
    public class MindeeDebugController : ControllerBase
    {
        private readonly ILogger<MindeeDebugController> _logger;
        private readonly ReceiptDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public MindeeDebugController(
            ILogger<MindeeDebugController> logger,
            ReceiptDbContext context,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _context = context;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        [HttpGet("config")]
        public IActionResult CheckMindeeConfig()
        {
            try
            {
                var mindeeApiKey = _configuration["Mindee:ApiKey"];
                var mindeeSection = _configuration.GetSection("Mindee");

                var allMindeeKeys = new Dictionary<string, string>();
                foreach (var child in mindeeSection.GetChildren())
                {
                    allMindeeKeys[child.Key] = child.Value ?? "null";
                }

                return Ok(new
                {
                    success = true,
                    mindeeConfiguration = new
                    {
                        hasMindeeApiKey = !string.IsNullOrEmpty(mindeeApiKey),
                        apiKeyLength = mindeeApiKey?.Length ?? 0,
                        apiKeyPreview = mindeeApiKey?.Length > 10 ? mindeeApiKey[..10] + "..." : mindeeApiKey,
                        allMindeeSettings = allMindeeKeys,
                        environmentVariables = new
                        {
                            MINDEE_API_KEY = Environment.GetEnvironmentVariable("MINDEE_API_KEY")?.Length > 0 ? "Set" : "Not Set",
                            Mindee__ApiKey = Environment.GetEnvironmentVariable("Mindee__ApiKey")?.Length > 0 ? "Set" : "Not Set"
                        }
                    },
                    allConfigKeys = _configuration.AsEnumerable()
                        .Where(kvp => kvp.Key.ToLower().Contains("mindee"))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.Length > 10 ? kvp.Value[..10] + "..." : kvp.Value),
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

        [HttpGet("recent-receipts")]
        public async Task<IActionResult> GetRecentReceipts()
        {
            try
            {
                var receipts = await _context.Receipts
                    .Include(r => r.Attachments)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(10)
                    .ToListAsync(); // Erst materialisieren, dann projizieren

                var receiptData = receipts.Select(r => new
                {
                    r.Id,
                    r.EmailId,
                    r.Merchant,
                    r.OriginalEmailSubject,
                    r.CreatedAt,
                    r.IsProcessed,
                    AttachmentCount = r.Attachments.Count,
                    Attachments = r.Attachments.Select(a => new
                    {
                        a.Id,
                        a.FileName,
                        a.ContentType,
                        a.FileSize,
                        HasContent = a.Content != null && a.Content.Length > 0,
                        HasProcessingDetails = !string.IsNullOrEmpty(a.ProcessingDetails),
                        ProcessingDetailsPreview = !string.IsNullOrEmpty(a.ProcessingDetails) ?
                            (a.ProcessingDetails.Length > 100 ? a.ProcessingDetails.Substring(0, 100) + "..." : a.ProcessingDetails) : null
                    }).ToList(),
                    HasExtractedText = !string.IsNullOrEmpty(r.ExtractedText),
                    r.Amount,
                    r.Currency
                }).ToList();

                return Ok(new
                {
                    success = true,
                    receiptsCount = receiptData.Count,
                    receipts = receiptData,
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

        [HttpPost("test-mindee")]
        public async Task<IActionResult> TestMindeeDirectly([FromForm] IFormFile? file)
        {
            var testId = Guid.NewGuid().ToString("N")[..8];

            try
            {
                _logger.LogInformation("🧪 Manual Mindee test {TestId} started", testId);

                // 1. Check API Key
                var mindeeApiKey = _configuration["Mindee:ApiKey"];
                if (string.IsNullOrEmpty(mindeeApiKey))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Mindee API key not configured",
                        testId = testId,
                        configHelp = "Set Mindee__ApiKey in app settings or environment variable"
                    });
                }

                _logger.LogInformation("✅ {TestId} - API Key found (length: {Length})", testId, mindeeApiKey.Length);

                // 2. Check file
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "No file provided",
                        testId = testId,
                        usage = "POST with form-data and 'file' field containing PDF"
                    });
                }

                _logger.LogInformation("📄 {TestId} - File received: {Name} ({Size} bytes, {ContentType})",
                    testId, file.FileName, file.Length, file.ContentType);

                // 3. Call Mindee API
                var url = "https://api.mindee.net/v1/products/mindee/expense_receipts/v5/predict";

                using var formContent = new MultipartFormDataContent();

                // Read file content
                using var memoryStream = new MemoryStream();
                await file.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();

                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "application/pdf");
                formContent.Add(fileContent, "document", file.FileName ?? "test.pdf");

                // Set headers
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Token {mindeeApiKey}");

                _logger.LogInformation("🚀 {TestId} - Calling Mindee API: {Url}", testId, url);

                var response = await _httpClient.PostAsync(url, formContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("📥 {TestId} - Mindee response: {StatusCode}, Length: {Length}",
                    testId, response.StatusCode, responseContent.Length);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ {TestId} - Mindee API success!", testId);

                    object? parsedResponse = null;
                    try
                    {
                        parsedResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    }
                    catch (Exception parseEx)
                    {
                        _logger.LogWarning("⚠️ {TestId} - Could not parse Mindee response as JSON: {Error}", testId, parseEx.Message);
                    }

                    return Ok(new
                    {
                        success = true,
                        testId = testId,
                        message = "Mindee API call successful",
                        fileInfo = new
                        {
                            fileName = file.FileName,
                            fileSize = file.Length,
                            contentType = file.ContentType
                        },
                        mindeeResponse = new
                        {
                            statusCode = (int)response.StatusCode,
                            contentLength = responseContent.Length,
                            rawResponse = responseContent,
                            parsedResponse = parsedResponse
                        },
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    _logger.LogError("❌ {TestId} - Mindee API error: {StatusCode}", testId, response.StatusCode);

                    return StatusCode((int)response.StatusCode, new
                    {
                        success = false,
                        testId = testId,
                        error = "Mindee API error",
                        statusCode = (int)response.StatusCode,
                        responseBody = responseContent,
                        timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {TestId} - Exception during Mindee test", testId);
                return StatusCode(500, new
                {
                    success = false,
                    testId = testId,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        [HttpPost("process-attachment/{attachmentId}")]
        public async Task<IActionResult> ProcessSpecificAttachment(int attachmentId)
        {
            var processId = Guid.NewGuid().ToString("N")[..8];

            try
            {
                _logger.LogInformation("🔄 Processing attachment {AttachmentId} manually (Process: {ProcessId})", attachmentId, processId);

                // 1. Get attachment from database
                var attachment = await _context.ReceiptAttachments
                    .Include(a => a.Receipt)
                    .FirstOrDefaultAsync(a => a.Id == attachmentId);

                if (attachment == null)
                {
                    return NotFound(new
                    {
                        success = false,
                        error = "Attachment not found",
                        attachmentId = attachmentId,
                        processId = processId
                    });
                }

                _logger.LogInformation("📎 {ProcessId} - Found attachment: {Name} ({ContentType}, {Size} bytes)",
                    processId, attachment.FileName, attachment.ContentType, attachment.Content?.Length ?? 0);

                // 2. Check if it's a PDF
                if (attachment.ContentType?.Contains("pdf") != true)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Attachment is not a PDF",
                        contentType = attachment.ContentType,
                        processId = processId
                    });
                }

                // 3. Check if content exists
                if (attachment.Content == null || attachment.Content.Length == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "Attachment has no content",
                        processId = processId
                    });
                }

                // 4. Call Mindee
                var mindeeResult = await CallMindeeForAttachment(attachment, processId);

                if (mindeeResult != null)
                {
                    // 5. Update attachment with result
                    attachment.ProcessingDetails = JsonSerializer.Serialize(mindeeResult);
                    await _context.SaveChangesAsync();

                    // 6. Update receipt with extracted data
                    await UpdateReceiptWithMindeeData(attachment.Receipt, mindeeResult, processId);

                    return Ok(new
                    {
                        success = true,
                        processId = processId,
                        message = "Attachment processed successfully with Mindee",
                        attachmentId = attachmentId,
                        receiptId = attachment.ReceiptId,
                        mindeeResult = mindeeResult,
                        timestamp = DateTime.UtcNow
                    });
                }
                else
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        error = "Mindee processing failed",
                        processId = processId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {ProcessId} - Error processing attachment {AttachmentId}", processId, attachmentId);
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message,
                    processId = processId
                });
            }
        }

        private async Task<object?> CallMindeeForAttachment(ReceiptAttachment attachment, string processId)
        {
            try
            {
                var mindeeApiKey = _configuration["Mindee:ApiKey"];
                if (string.IsNullOrEmpty(mindeeApiKey))
                {
                    _logger.LogError("❌ {ProcessId} - No Mindee API key configured", processId);
                    return null;
                }

                _logger.LogInformation("🤖 {ProcessId} - Calling Mindee API", processId);

                var url = "https://api.mindee.net/v1/products/mindee/expense_receipts/v5/predict";

                using var formContent = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(attachment.Content!);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(attachment.ContentType ?? "application/pdf");
                formContent.Add(fileContent, "document", attachment.FileName ?? "receipt.pdf");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Token {mindeeApiKey}");

                var response = await _httpClient.PostAsync(url, formContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("📥 {ProcessId} - Mindee response: {StatusCode}, Length: {Length}",
                    processId, response.StatusCode, responseContent.Length);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ {ProcessId} - Mindee success", processId);
                    return JsonSerializer.Deserialize<JsonElement>(responseContent);
                }
                else
                {
                    _logger.LogError("❌ {ProcessId} - Mindee error: {StatusCode} - {Response}",
                        processId, response.StatusCode, responseContent);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {ProcessId} - Mindee API exception", processId);
                return null;
            }
        }

        private async Task UpdateReceiptWithMindeeData(Receipt receipt, object mindeeResult, string processId)
        {
            try
            {
                _logger.LogInformation("🔄 {ProcessId} - Updating receipt {ReceiptId} with Mindee data", processId, receipt.Id);

                var jsonElement = (JsonElement)mindeeResult;

                if (jsonElement.TryGetProperty("document", out var document) &&
                    document.TryGetProperty("inference", out var inference) &&
                    inference.TryGetProperty("prediction", out var prediction))
                {
                    // Extract amount
                    if (prediction.TryGetProperty("total_amount", out var totalAmount) &&
                        totalAmount.TryGetProperty("value", out var amountValue) &&
                        amountValue.ValueKind == JsonValueKind.Number)
                    {
                        receipt.Amount = amountValue.GetDecimal();
                        _logger.LogInformation("💰 {ProcessId} - Extracted amount: {Amount}", processId, receipt.Amount);
                    }

                    // Extract currency
                    if (prediction.TryGetProperty("locale", out var locale) &&
                        locale.TryGetProperty("currency", out var currency) &&
                        currency.ValueKind == JsonValueKind.String)
                    {
                        receipt.Currency = currency.GetString();
                        _logger.LogInformation("💱 {ProcessId} - Extracted currency: {Currency}", processId, receipt.Currency);
                    }

                    // Extract date
                    if (prediction.TryGetProperty("date", out var dateProperty) &&
                        dateProperty.TryGetProperty("value", out var dateValue) &&
                        dateValue.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(dateValue.GetString(), out var transactionDate))
                    {
                        receipt.TransactionDate = transactionDate;
                        _logger.LogInformation("📅 {ProcessId} - Extracted date: {Date}", processId, receipt.TransactionDate);
                    }

                    // Extract supplier
                    if (prediction.TryGetProperty("supplier_name", out var supplier) &&
                        supplier.TryGetProperty("value", out var supplierValue) &&
                        supplierValue.ValueKind == JsonValueKind.String)
                    {
                        var supplierName = supplierValue.GetString();
                        if (!string.IsNullOrEmpty(supplierName))
                        {
                            receipt.Merchant = supplierName;
                            _logger.LogInformation("🏪 {ProcessId} - Extracted merchant: {Merchant}", processId, receipt.Merchant);
                        }
                    }

                    receipt.ExtractedText = JsonSerializer.Serialize(mindeeResult);
                }

                receipt.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ {ProcessId} - Receipt updated successfully", processId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {ProcessId} - Error updating receipt", processId);
            }
        }

        [HttpGet("why-no-mindee")]
        public async Task<IActionResult> WhyNoMindee()
        {
            try
            {
                var recentReceipts = await _context.Receipts
                    .Include(r => r.Attachments)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(5)
                    .ToListAsync();

                var analysis = recentReceipts.Select(r => new
                {
                    r.Id,
                    r.OriginalEmailSubject,
                    r.CreatedAt,
                    AttachmentCount = r.Attachments.Count,
                    AttachmentAnalysis = r.Attachments.Select(a => new
                    {
                        a.Id,
                        a.FileName,
                        a.ContentType,
                        HasContent = a.Content != null && a.Content.Length > 0,
                        ContentSize = a.Content?.Length ?? 0,
                        IsPdf = a.ContentType?.Contains("pdf") == true,
                        HasProcessingDetails = !string.IsNullOrEmpty(a.ProcessingDetails),
                        WhyNoMindee = GetWhyNoMindee(a)
                    }).ToList(),
                    r.Amount,
                    r.Currency,
                    HasExtractedText = !string.IsNullOrEmpty(r.ExtractedText)
                }).ToList();

                var mindeeApiKey = _configuration["Mindee:ApiKey"];

                return Ok(new
                {
                    success = true,
                    mindeeConfiguration = new
                    {
                        hasMindeeApiKey = !string.IsNullOrEmpty(mindeeApiKey),
                        apiKeyLength = mindeeApiKey?.Length ?? 0
                    },
                    recentReceiptsAnalysis = analysis,
                    possibleReasons = new[]
                    {
                        "No Mindee API key configured",
                        "No PDF attachments in emails",
                        "Attachments have no content",
                        "Wrong ContentType detection",
                        "Mindee API calls failing",
                        "Processing exceptions being caught"
                    },
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

        private static string GetWhyNoMindee(ReceiptAttachment attachment)
        {
            if (attachment.Content == null || attachment.Content.Length == 0)
                return "No content";

            if (attachment.ContentType?.Contains("pdf") != true)
                return $"Not PDF (ContentType: {attachment.ContentType})";

            if (!string.IsNullOrEmpty(attachment.ProcessingDetails))
                return "Already processed";

            return "Should have been processed - check logs";
        }
    }
}