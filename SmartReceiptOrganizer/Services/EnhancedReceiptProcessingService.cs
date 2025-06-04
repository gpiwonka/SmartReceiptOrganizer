// Services/EnhancedReceiptProcessingService.cs - Integration von Mindee
using SmartReceiptOrganizer.Core.Interfaces;
using SmartReceiptOrganizer.Core.Models.Postmark;

using SmartReceiptOrganizer.Core.Models;
using SmartReceiptOrganizer.Services;

namespace SmartReceiptOrganizer.Services
{
    public class EnhancedReceiptProcessingService : IReceiptProcessingService
    {
        private readonly IReceiptRepository _receiptRepository;
        private readonly IReceiptParsingService _textParsingService;
        private readonly IMindeeParsingService _mindeeParsingService;
        private readonly ILogger<EnhancedReceiptProcessingService> _logger;

        public EnhancedReceiptProcessingService(
            IReceiptRepository receiptRepository,
            IReceiptParsingService textParsingService,
            IMindeeParsingService mindeeParsingService,
            ILogger<EnhancedReceiptProcessingService> logger)
        {
            _receiptRepository = receiptRepository;
            _textParsingService = textParsingService;
            _mindeeParsingService = mindeeParsingService;
            _logger = logger;
        }

        public async Task<ReceiptProcessingResult> ProcessInboundEmailAsync(PostmarkInboundMessage message)
        {
            try
            {
                _logger.LogInformation("Processing email from {From} with subject: {Subject}",
                    message.From, message.Subject);

                // 1. Prüfen ob bereits verarbeitet
                var existingReceipt = await _receiptRepository.GetByEmailIdAsync(message.MessageId);
                if (existingReceipt != null)
                {
                    _logger.LogInformation("Email {MessageId} already processed as receipt {ReceiptId}",
                        message.MessageId, existingReceipt.Id);
                    return new ReceiptProcessingResult
                    {
                        IsSuccess = true,
                        ReceiptId = existingReceipt.Id,
                        Message = "Already processed"
                    };
                }

                // 2. Text-basierte Erkennung (schnell und kostenfrei)
                var isReceiptEmail = IsReceiptEmail(message);
                if (!isReceiptEmail)
                {
                    _logger.LogInformation("Email {MessageId} does not appear to be a receipt", message.MessageId);
                    return new ReceiptProcessingResult
                    {
                        IsSuccess = true,
                        Message = "Email processed but no receipt detected"
                    };
                }

                // 3. Text-Parsing aus Email-Body
                var textExtractedData = await ExtractReceiptDataFromTextAsync(message);

                // 4. PDF/Image-Parsing mit Mindee (falls Attachments vorhanden)
                MindeeReceiptResult mindeeResult = null;
                if (message.Attachments?.Any() == true)
                {
                    mindeeResult = await ProcessAttachmentsWithMindeeAsync(message.Attachments);
                }

                // 5. Daten kombinieren (Mindee hat Priorität bei Konflikten)
                var finalReceiptData = CombineExtractionResults(textExtractedData, mindeeResult, message);

                // 6. Receipt erstellen und speichern
                var receipt = await CreateReceiptFromExtractedDataAsync(finalReceiptData, message);

                // 7. Attachments hinzufügen
                if (message.Attachments?.Any() == true)
                {
                    await AddAttachmentsToReceiptAsync(receipt, message.Attachments);
                }

                var savedReceipt = await _receiptRepository.CreateAsync(receipt);

                _logger.LogInformation("Successfully created receipt {ReceiptId} for email {MessageId} " +
                    "(Text: {HasText}, Mindee: {HasMindee}, Confidence: {Confidence})",
                    savedReceipt.Id, message.MessageId,
                    textExtractedData != null, mindeeResult?.IsSuccess == true,
                    mindeeResult?.OverallConfidence ?? 0);

                return new ReceiptProcessingResult
                {
                    IsSuccess = true,
                    ReceiptId = savedReceipt.Id,
                    ExtractedData = ConvertToExtractedReceiptData(finalReceiptData),
                    Message = $"Receipt successfully processed " +
                             $"(Confidence: {(mindeeResult?.OverallConfidence ?? 0):P0})"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing receipt from email {MessageId}", message.MessageId);
                return new ReceiptProcessingResult
                {
                    IsSuccess = false,
                    Message = "Error processing receipt",
                    Errors = new List<string> { ex.Message }
                };
            }
        }

        private async Task<MindeeReceiptResult> ProcessAttachmentsWithMindeeAsync(List<PostmarkAttachment> attachments)
        {
            foreach (var attachment in attachments)
            {
                try
                {
                    if (!IsPotentialReceiptAttachment(attachment))
                        continue;

                    var content = Convert.FromBase64String(attachment.Content);

                    MindeeReceiptResult result = null;

                    if (attachment.ContentType?.StartsWith("application/pdf") == true)
                    {
                        result = await _mindeeParsingService.ParseReceiptFromPdfAsync(content, attachment.Name);
                    }
                    else if (attachment.ContentType?.StartsWith("image/") == true)
                    {
                        result = await _mindeeParsingService.ParseReceiptFromImageAsync(content, attachment.Name);
                    }

                    if (result?.IsSuccess == true && result.Amount.HasValue)
                    {
                        _logger.LogInformation("Successfully parsed attachment {FileName} with Mindee " +
                            "(Amount: {Amount}, Confidence: {Confidence})",
                            attachment.Name, result.Amount, result.OverallConfidence);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing attachment {FileName} with Mindee", attachment.Name);
                }
            }

            return null;
        }

        private CombinedReceiptData CombineExtractionResults(
            ExtractedReceiptData textData,
            MindeeReceiptResult mindeeResult,
            PostmarkInboundMessage message)
        {
            var combined = new CombinedReceiptData();

            // Mindee-Daten haben Priorität, falls verfügbar und vertrauenswürdig
            if (mindeeResult?.IsSuccess == true && mindeeResult.OverallConfidence > 0.7)
            {
                combined.Amount = mindeeResult.Amount ?? textData?.Amount;
                combined.Currency = mindeeResult.Currency ?? textData?.Currency ?? "EUR";
                combined.TransactionDate = mindeeResult.TransactionDate ?? textData?.TransactionDate;
                combined.Merchant = mindeeResult.Merchant ?? textData?.Merchant;
                combined.Category = mindeeResult.Category ?? textData?.Category;
                combined.DataSource = "Mindee (Primary)";
                combined.Confidence = mindeeResult.OverallConfidence;
                combined.AdditionalData = mindeeResult.AdditionalData;
            }
            else
            {
                // Fallback zu Text-Parsing
                combined.Amount = textData?.Amount;
                combined.Currency = textData?.Currency ?? "EUR";
                combined.TransactionDate = textData?.TransactionDate;
                combined.Merchant = textData?.Merchant;
                combined.Category = textData?.Category;
                combined.DataSource = "Text Parsing";
                combined.Confidence = 0.5; // Lower confidence for text-only
            }

            // Fallbacks für fehlende Daten
            combined.Merchant ??= ExtractMerchantFromSender(message.From);
            combined.Category ??= DetermineCategory(combined.Merchant);
            combined.TransactionDate ??= message.Date;

            return combined;
        }

        // Helper Methods
        private bool IsReceiptEmail(PostmarkInboundMessage message)
        {
            var receiptKeywords = new[]
            {
                "rechnung", "invoice", "receipt", "beleg", "quittung",
                "kaufbeleg", "kassenbon", "zahlungsbeleg", "bill",
                "payment confirmation", "zahlungsbestätigung", "order confirmation",
                "bestellbestätigung", "purchase", "kauf", "total", "gesamt"
            };

            var content = $"{message.Subject} {message.TextBody} {message.HtmlBody}".ToLowerInvariant();

            return receiptKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                   message.Attachments?.Any(a => IsPotentialReceiptAttachment(a)) == true;
        }

        private bool IsPotentialReceiptAttachment(PostmarkAttachment attachment)
        {
            if (attachment.ContentType?.StartsWith("application/pdf") == true) return true;
            if (attachment.ContentType?.StartsWith("image/") == true) return true;

            var fileName = attachment.Name?.ToLowerInvariant() ?? "";
            return fileName.Contains("receipt") || fileName.Contains("rechnung") ||
                   fileName.Contains("invoice") || fileName.Contains("beleg");
        }

        private async Task<ExtractedReceiptData> ExtractReceiptDataFromTextAsync(PostmarkInboundMessage message)
        {
            var content = message.TextBody ?? message.HtmlBody ?? "";

            return new ExtractedReceiptData
            {
                Amount = _textParsingService.ExtractAmount(content),
                Currency = _textParsingService.ExtractCurrency(content),
                TransactionDate = _textParsingService.ExtractDate(content),
                Merchant = _textParsingService.ExtractMerchant(content, message.From)
            };
        }

        private string ExtractMerchantFromSender(string fromEmail)
        {
            // Implementation wie vorhin...
            if (string.IsNullOrEmpty(fromEmail)) return "Unknown";

            var emailMatch = System.Text.RegularExpressions.Regex.Match(fromEmail, @"@([^.]+)");
            if (emailMatch.Success)
            {
                return emailMatch.Groups[1].Value.ToTitleCase();
            }

            return fromEmail;
        }

        private string DetermineCategory(string merchant)
        {
            // Implementation wie vorhin...
            if (string.IsNullOrEmpty(merchant)) return "Sonstiges";

            merchant = merchant.ToLowerInvariant();

            if (merchant.Contains("amazon") || merchant.Contains("ebay"))
                return "Online Shopping";
            if (merchant.Contains("rewe") || merchant.Contains("edeka"))
                return "Lebensmittel";
            if (merchant.Contains("shell") || merchant.Contains("aral"))
                return "Tankstelle";

            return "Sonstiges";
        }

        private async Task<Receipt> CreateReceiptFromExtractedDataAsync(CombinedReceiptData data, PostmarkInboundMessage message)
        {
            return new Receipt
            {
                EmailId = message.MessageId,
                Merchant = data.Merchant ?? "Unknown",
                Amount = data.Amount ?? 0,
                Currency = data.Currency ?? "EUR",
                TransactionDate = data.TransactionDate ?? message.Date,
                ReceivedDate = message.Date,
                Category = data.Category ?? "Sonstiges",
                OriginalEmailSubject = message.Subject,
                OriginalEmailBody = message.TextBody ?? message.HtmlBody,
                Attachments = new List<ReceiptAttachment>()
            };
        }

        private async Task AddAttachmentsToReceiptAsync(Receipt receipt, List<PostmarkAttachment> attachments)
        {
            foreach (var attachment in attachments)
            {
                if (IsPotentialReceiptAttachment(attachment))
                {
                    var receiptAttachment = new ReceiptAttachment
                    {
                        FileName = attachment.Name,
                        ContentType = attachment.ContentType,
                        Content = Convert.FromBase64String(attachment.Content)
                    };
                    receipt.Attachments.Add(receiptAttachment);
                }
            }
        }

        private ExtractedReceiptData ConvertToExtractedReceiptData(CombinedReceiptData combined)
        {
            return new ExtractedReceiptData
            {
                Amount = combined.Amount,
                Currency = combined.Currency,
                TransactionDate = combined.TransactionDate,
                Merchant = combined.Merchant,
                Category = combined.Category
            };
        }
    }

    // Helper Classes
    public class CombinedReceiptData
    {
        public decimal? Amount { get; set; }
        public string Currency { get; set; }
        public DateTime? TransactionDate { get; set; }
        public string Merchant { get; set; }
        public string Category { get; set; }
        public string DataSource { get; set; }
        public double Confidence { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }
}