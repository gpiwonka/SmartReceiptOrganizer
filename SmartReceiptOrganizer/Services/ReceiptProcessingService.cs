using SmartReceiptOrganizer.Core.Interfaces;
using SmartReceiptOrganizer.Core.Models.Postmark;
using SmartReceiptOrganizer.Core.Models;
using System.Text.RegularExpressions;
using System.Text;

namespace SmartReceiptOrganizer.Services
{
    public class ReceiptProcessingService : IReceiptProcessingService
    {
        private readonly IReceiptRepository _receiptRepository;
        private readonly IReceiptParsingService _parsingService;
        private readonly ILogger<ReceiptProcessingService> _logger;

        // Keywords für Receipt-Erkennung (Deutsch & Englisch)
        private readonly string[] _receiptKeywords = {
            "rechnung", "invoice", "receipt", "beleg", "quittung",
            "kaufbeleg", "kassenbon", "zahlungsbeleg", "bill",
            "payment confirmation", "zahlungsbestätigung", "order confirmation",
            "bestellbestätigung", "purchase", "kauf", "total", "gesamt",
            "betrag", "amount", "summe", "paypal", "stripe", "bezahlt"
        };

        public ReceiptProcessingService(
            IReceiptRepository receiptRepository,
            IReceiptParsingService parsingService,
            ILogger<ReceiptProcessingService> logger)
        {
            _receiptRepository = receiptRepository;
            _parsingService = parsingService;
            _logger = logger;
        }

        public async Task<ReceiptProcessingResult> ProcessInboundEmailAsync(PostmarkInboundMessage message)
        {
            try
            {
                _logger.LogInformation("Processing email from {From} with subject: {Subject}",
                    message.From, message.Subject);

                // 1. Prüfen ob Email ein Receipt sein könnte
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

                // 2. Receipt-Daten extrahieren
                var extractedData = await ExtractReceiptDataAsync(message);

                // 3. Receipt in Datenbank speichern
                var receipt = new Receipt
                {
                    EmailId = message.MessageId,
                    Merchant = extractedData.Merchant ?? ExtractMerchantFromSender(message.From),
                    Amount = extractedData.Amount ?? 0,
                    Currency = extractedData.Currency ?? "EUR", // Default für Deutschland
                    TransactionDate = extractedData.TransactionDate ?? message.Date,
                    ReceivedDate = message.Date,
                    Category = extractedData.Category ?? DetermineCategory(extractedData.Merchant ?? message.From),
                    OriginalEmailSubject = message.Subject,
                    OriginalEmailBody = message.TextBody ?? message.HtmlBody,
                    Attachments = new List<ReceiptAttachment>()
                };

                // 4. Attachments verarbeiten
                if (message.Attachments?.Any() == true)
                {
                    foreach (var attachment in message.Attachments)
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

                // 5. Receipt speichern
                var savedReceipt = await _receiptRepository.CreateAsync(receipt);

                _logger.LogInformation("Successfully created receipt {ReceiptId} for email {MessageId}",
                    savedReceipt.Id, message.MessageId);

                return new ReceiptProcessingResult
                {
                    IsSuccess = true,
                    ReceiptId = savedReceipt.Id,
                    ExtractedData = extractedData,
                    Message = "Receipt successfully processed and saved"
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

        private bool IsReceiptEmail(PostmarkInboundMessage message)
        {
            var content = $"{message.Subject} {message.TextBody} {message.HtmlBody}".ToLowerInvariant();

            return _receiptKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
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

        private async Task<ExtractedReceiptData> ExtractReceiptDataAsync(PostmarkInboundMessage message)
        {
            var extractedData = new ExtractedReceiptData();
            var content = message.TextBody ?? message.HtmlBody ?? "";

            // Text-Parsing für verschiedene Receipt-Formate
            extractedData.Amount = _parsingService.ExtractAmount(content);
            extractedData.Currency = _parsingService.ExtractCurrency(content);
            extractedData.TransactionDate = _parsingService.ExtractDate(content);
            extractedData.Merchant = _parsingService.ExtractMerchant(content, message.From);

            return extractedData;
        }

        private string ExtractMerchantFromSender(string fromEmail)
        {
            if (string.IsNullOrEmpty(fromEmail)) return "Unknown";

            // Versuche Domain als Merchant zu extrahieren
            var emailMatch = Regex.Match(fromEmail, @"@([^.]+)");
            if (emailMatch.Success)
            {
                return emailMatch.Groups[1].Value.ToTitleCase();
            }

            return fromEmail;
        }

        private string DetermineCategory(string merchant)
        {
            if (string.IsNullOrEmpty(merchant)) return "Sonstiges";

            merchant = merchant.ToLowerInvariant();

            // Einfache Kategorisierung basierend auf Merchant-Namen
            if (merchant.Contains("amazon") || merchant.Contains("ebay") || merchant.Contains("shop"))
                return "Online Shopping";
            if (merchant.Contains("rewe") || merchant.Contains("edeka") || merchant.Contains("aldi") || merchant.Contains("lidl"))
                return "Lebensmittel";
            if (merchant.Contains("shell") || merchant.Contains("esso") || merchant.Contains("aral"))
                return "Tankstelle";
            if (merchant.Contains("restaurant") || merchant.Contains("pizza") || merchant.Contains("mcdonald"))
                return "Restaurants";
            if (merchant.Contains("spotify") || merchant.Contains("netflix") || merchant.Contains("apple"))
                return "Abonnements";

            return "Sonstiges";
        }
    }
}