

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartReceiptOrganizer.Core.Models;
using SmartReceiptOrganizer.Core.Models.Postmark;
using SmartReceiptOrganizer.Data;
using System.Text;
using System.Text.Json;

namespace SmartReceiptOrganizer.Controllers
{
    [ApiController]
    [Route("api/postmark")]
    public class PostmarkReceiptController : ControllerBase
    {
        private readonly ILogger<PostmarkReceiptController> _logger;
        private readonly ReceiptDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public PostmarkReceiptController(
            ILogger<PostmarkReceiptController> logger,
            ReceiptDbContext context,
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _context = context;
            _httpClient = httpClient;
            _configuration = configuration;
        }

        [HttpPost("inbound")]
        public async Task<IActionResult> ProcessInboundEmail()
        {
            var requestId = Guid.NewGuid().ToString("N")[..8];

            try
            {
                _logger.LogInformation("🚀 Receipt Processing {RequestId} - Started", requestId);

                // 1. READ RAW JSON
                using var reader = new StreamReader(Request.Body);
                var bodyJson = await reader.ReadToEndAsync();

                _logger.LogInformation("📄 {RequestId} - Raw JSON length: {Length}", requestId, bodyJson.Length);

                // 2. SIMPLE JSON PARSE (DateTime as string)
                PostmarkInboundMessage? postmarkMessage = null;

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        WriteIndented = true
                    };

                    postmarkMessage = JsonSerializer.Deserialize<PostmarkInboundMessage>(bodyJson, options);

                    if (postmarkMessage != null)
                    {
                        // PARSE THE DATE MANUALLY (100% safe)
                        postmarkMessage.ParseDate();

                        _logger.LogInformation("✅ {RequestId} - JSON parsing successful, DateString='{DateString}', ParsedDate='{ParsedDate}'",
                            requestId, postmarkMessage.DateString, postmarkMessage.Date.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError("❌ {RequestId} - JSON parsing failed: {Error}", requestId, ex.Message);

                    var problemArea = bodyJson.Length > 1000 ? bodyJson.Substring(0, 1000) + "..." : bodyJson;
                    _logger.LogError("🔍 {RequestId} - Problematic JSON: {Json}", requestId, problemArea);

                    return BadRequest(new
                    {
                        success = false,
                        requestId = requestId,
                        error = "Failed to parse Postmark JSON",
                        details = ex.Message,
                        jsonPreview = problemArea
                    });
                }

                if (postmarkMessage == null)
                {
                    _logger.LogError("❌ {RequestId} - Parsed message is null", requestId);
                    return BadRequest("Invalid Postmark message format");
                }

                _logger.LogInformation("📧 {RequestId} - Parsed email: Subject='{Subject}', From='{From}', Date='{Date}', Attachments={Count}",
                    requestId,
                    postmarkMessage.Subject,
                    postmarkMessage.From,
                    postmarkMessage.Date.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    postmarkMessage.Attachments?.Count ?? 0);

                // 3. CHECK FOR DUPLICATES
                var existingReceipt = await _context.Receipts
                    .FirstOrDefaultAsync(r => r.EmailId == postmarkMessage.MessageId);

                if (existingReceipt != null)
                {
                    _logger.LogWarning("⚠️ {RequestId} - Duplicate email ID: {MessageId}", requestId, postmarkMessage.MessageId);
                    return Ok(new
                    {
                        success = true,
                        message = "Email already processed",
                        receiptId = existingReceipt.Id,
                        requestId = requestId
                    });
                }

                // 4. CREATE RECEIPT RECORD
                var receipt = new Receipt
                {
                    EmailId = postmarkMessage.MessageId ?? Guid.NewGuid().ToString(),
                    Merchant = ExtractMerchantFromEmail(postmarkMessage),
                    OriginalEmailSubject = postmarkMessage.Subject,
                    OriginalEmailBody = postmarkMessage.TextBody ?? postmarkMessage.HtmlBody,
                    ReceivedDate = postmarkMessage.Date != default ? postmarkMessage.Date : DateTime.UtcNow,
                    IsProcessed = false,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Receipts.Add(receipt);
                await _context.SaveChangesAsync();

                _logger.LogInformation("💾 {RequestId} - Receipt saved: ID={ReceiptId}, Merchant='{Merchant}'",
                    requestId, receipt.Id, receipt.Merchant);

                // 5. PROCESS ATTACHMENTS
                var processedAttachments = 0;
                var mindeeResults = new List<object>();

                if (postmarkMessage.Attachments?.Any() == true)
                {
                    _logger.LogInformation("📎 {RequestId} - Processing {Count} attachments", requestId, postmarkMessage.Attachments.Count);

                    foreach (var attachment in postmarkMessage.Attachments)
                    {
                        try
                        {
                            var attachmentResult = await ProcessAttachment(receipt.Id, attachment, requestId);
                            if (attachmentResult != null)
                            {
                                mindeeResults.Add(attachmentResult);
                                processedAttachments++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "❌ {RequestId} - Failed to process attachment: {Name}", requestId, attachment.Name);
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("📎 {RequestId} - No attachments to process", requestId);
                }

                // 6. UPDATE RECEIPT WITH EXTRACTED DATA
                if (mindeeResults.Any())
                {
                    var bestResult = mindeeResults.First();
                    await UpdateReceiptWithMindeeData(receipt.Id, bestResult, requestId);
                }

                // 7. MARK AS PROCESSED
                receipt.IsProcessed = true;
                receipt.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ {RequestId} - Processing completed: ReceiptId={ReceiptId}, Attachments={Count}, MindeeResults={MindeeCount}",
                    requestId, receipt.Id, processedAttachments, mindeeResults.Count);

                // 8. SUCCESS RESPONSE
                return Ok(new
                {
                    success = true,
                    requestId = requestId,
                    receiptId = receipt.Id,
                    merchant = receipt.Merchant,
                    subject = receipt.OriginalEmailSubject,
                    receivedDate = receipt.ReceivedDate.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    attachmentsProcessed = processedAttachments,
                    mindeeResults = mindeeResults.Count,
                    extractedAmount = receipt.Amount,
                    extractedCurrency = receipt.Currency,
                    dateStringFromPostmark = postmarkMessage.DateString,
                    parsedDate = postmarkMessage.Date.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {RequestId} - Critical error during receipt processing", requestId);
                return StatusCode(500, new
                {
                    success = false,
                    requestId = requestId,
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }

        private async Task<object?> ProcessAttachment(int receiptId, PostmarkAttachment attachment, string requestId)
        {
            try
            {
                _logger.LogInformation("📎 {RequestId} - Processing attachment: {Name} ({ContentType})",
                    requestId, attachment.Name, attachment.ContentType);

                if (string.IsNullOrEmpty(attachment.Content))
                {
                    _logger.LogWarning("⚠️ {RequestId} - Attachment has no content: {Name}", requestId, attachment.Name);
                    return null;
                }

                // SAVE ATTACHMENT TO DATABASE
                var attachmentRecord = new ReceiptAttachment
                {
                    ReceiptId = receiptId,
                    FileName = attachment.Name,
                    ContentType = attachment.ContentType,
                    Content = Convert.FromBase64String(attachment.Content),
                    FileSize = attachment.ContentLength,
                    CreatedAt = DateTime.UtcNow
                };

                _context.ReceiptAttachments.Add(attachmentRecord);
                await _context.SaveChangesAsync();

                _logger.LogInformation("💾 {RequestId} - Attachment saved: ID={AttachmentId}, Size={Size} bytes",
                    requestId, attachmentRecord.Id, attachmentRecord.Content?.Length ?? 0);

                // SMART PDF DETECTION
                var isPdf = IsPdfAttachment(attachment);

                if (isPdf)
                {
                    _logger.LogInformation("🔍 {RequestId} - Detected PDF attachment (ContentType: {ContentType}, Name: {Name}) - sending to Mindee",
                        requestId, attachment.ContentType, attachment.Name);

                    var mindeeResult = await AnalyzeWithMindee(attachment, requestId);
                    if (mindeeResult != null)
                    {
                        // Update attachment with Mindee result
                        attachmentRecord.ProcessingDetails = JsonSerializer.Serialize(mindeeResult);
                        await _context.SaveChangesAsync();

                        _logger.LogInformation("✅ {RequestId} - Mindee analysis completed and saved", requestId);
                        return mindeeResult;
                    }
                }
                else
                {
                    _logger.LogInformation("⏭️ {RequestId} - Skipping non-PDF attachment: ContentType={ContentType}, Name={Name}",
                        requestId, attachment.ContentType, attachment.Name);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {RequestId} - Error processing attachment: {Name}", requestId, attachment.Name);
                return null;
            }
        }

        /// <summary>
        /// Smart PDF detection - checks both ContentType and filename
        /// </summary>
        private static bool IsPdfAttachment(PostmarkAttachment attachment)
        {
            // 1. Check ContentType (standard way)
            if (attachment.ContentType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            // 2. Check filename extension (fallback for octet-stream)
            if (!string.IsNullOrEmpty(attachment.Name))
            {
                var fileName = attachment.Name.ToLowerInvariant();
                if (fileName.EndsWith(".pdf"))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<object?> AnalyzeWithMindee(PostmarkAttachment attachment, string requestId)
        {
            try
            {
                var mindeeApiKey = _configuration["Mindee:ApiKey"];
                if (string.IsNullOrEmpty(mindeeApiKey))
                {
                    _logger.LogWarning("⚠️ {RequestId} - Mindee API key not configured", requestId);
                    return null;
                }

                _logger.LogInformation("🤖 {RequestId} - Calling Mindee API for receipt analysis", requestId);

                var url = "https://api.mindee.net/v1/products/mindee/expense_receipts/v5/predict";

                using var formContent = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(Convert.FromBase64String(attachment.Content ?? ""));
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                formContent.Add(fileContent, "document", attachment.Name ?? "receipt.pdf");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Token {mindeeApiKey}");

                var response = await _httpClient.PostAsync(url, formContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("📥 {RequestId} - Mindee response: {StatusCode}, Length: {Length}",
                    requestId, response.StatusCode, responseContent.Length);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("✅ {RequestId} - Mindee analysis successful", requestId);
                    var mindeeResult = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    return mindeeResult;
                }
                else
                {
                    _logger.LogError("❌ {RequestId} - Mindee API error: {StatusCode} - {Response}",
                        requestId, response.StatusCode, responseContent);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {RequestId} - Exception calling Mindee API", requestId);
                return null;
            }
        }

        private async Task UpdateReceiptWithMindeeData(int receiptId, object mindeeResult, string requestId)
        {
            try
            {
                var receipt = await _context.Receipts.FindAsync(receiptId);
                if (receipt == null) return;

                _logger.LogInformation("🔄 {RequestId} - Updating receipt with Mindee data", requestId);

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
                        _logger.LogInformation("💰 {RequestId} - Extracted amount: {Amount}", requestId, receipt.Amount);
                    }

                    // Extract currency
                    if (prediction.TryGetProperty("locale", out var locale) &&
                        locale.TryGetProperty("currency", out var currency) &&
                        currency.ValueKind == JsonValueKind.String)
                    {
                        receipt.Currency = currency.GetString();
                        _logger.LogInformation("💱 {RequestId} - Extracted currency: {Currency}", requestId, receipt.Currency);
                    }

                    // Extract date
                    if (prediction.TryGetProperty("date", out var dateProperty) &&
                        dateProperty.TryGetProperty("value", out var dateValue) &&
                        dateValue.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(dateValue.GetString(), out var transactionDate))
                    {
                        receipt.TransactionDate = transactionDate;
                        _logger.LogInformation("📅 {RequestId} - Extracted date: {Date}", requestId, receipt.TransactionDate);
                    }

                    // Extract supplier (merchant)
                    if (prediction.TryGetProperty("supplier_name", out var supplier) &&
                        supplier.TryGetProperty("value", out var supplierValue) &&
                        supplierValue.ValueKind == JsonValueKind.String)
                    {
                        var supplierName = supplierValue.GetString();
                        if (!string.IsNullOrEmpty(supplierName))
                        {
                            receipt.Merchant = supplierName;
                            _logger.LogInformation("🏪 {RequestId} - Extracted merchant: {Merchant}", requestId, receipt.Merchant);
                        }
                    }

                    receipt.ExtractedText = JsonSerializer.Serialize(mindeeResult);
                }

                receipt.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ {RequestId} - Receipt updated with extracted data", requestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {RequestId} - Error updating receipt with Mindee data", requestId);
            }
        }

        private static string? ExtractMerchantFromEmail(PostmarkInboundMessage message)
        {
            // From Subject
            if (!string.IsNullOrEmpty(message.Subject))
            {
                var subject = message.Subject.ToLower();

                if (subject.Contains("rechnung") || subject.Contains("invoice"))
                {
                    var words = message.Subject.Split(' ');
                    foreach (var word in words)
                    {
                        if (word.Length > 2 && !word.ToLower().Contains("rechnung") && !word.ToLower().Contains("invoice"))
                        {
                            return word.Trim();
                        }
                    }
                }
            }

            // From Email Domain
            if (!string.IsNullOrEmpty(message.From))
            {
                try
                {
                    var emailParts = message.From.Split('@');
                    if (emailParts.Length > 1)
                    {
                        var domain = emailParts[1].Split('.')[0];
                        return char.ToUpper(domain[0]) + domain[1..].ToLower();
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            return "Unknown";
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                success = true,
                message = "Receipt processing controller is ready (with foolproof DateTime handling)",
                endpoints = new[]
                {
                    "POST /api/postmark/inbound - Main webhook endpoint with Mindee and string DateTime",
                    "GET  /api/postmark/test - This test endpoint"
                },
                configuration = new
                {
                    hasMindeeApiKey = !string.IsNullOrEmpty(_configuration["Mindee:ApiKey"]),
                    databaseConnected = _context.Database.CanConnect()
                },
                timestamp = DateTime.UtcNow
            });
        }
    }
}