// Services/MindeeReceiptParsingService.cs
using Mindee;
using Mindee.Input;
using Mindee.Product.Receipt;
using SmartReceiptOrganizer.Core.Interfaces;
using SmartReceiptOrganizer.Core.Models.Postmark;

using System.Text;

namespace SmartReceiptOrganizer.Services
{
    public interface IMindeeParsingService
    {
        Task<MindeeReceiptResult> ParseReceiptFromPdfAsync(byte[] pdfContent, string fileName);
        Task<MindeeReceiptResult> ParseReceiptFromImageAsync(byte[] imageContent, string fileName);
        Task<bool> IsReceiptDocumentAsync(byte[] content, string contentType);
    }

    public class MindeeParsingService : IMindeeParsingService
    {
        private readonly MindeeClient _mindeeClient;
        private readonly ILogger<MindeeParsingService> _logger;
        private readonly IConfiguration _configuration;

        public MindeeParsingService(
            ILogger<MindeeParsingService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            var apiKey = _configuration["Mindee:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Mindee API Key is not configured");
            }

            _mindeeClient = new MindeeClient(apiKey);
        }

        public async Task<MindeeReceiptResult> ParseReceiptFromPdfAsync(byte[] pdfContent, string fileName)
        {
            try
            {
                _logger.LogInformation("Starting Mindee PDF parsing for file: {FileName}", fileName);

                using var stream = new MemoryStream(pdfContent);
                var inputSource = new LocalInputSource(stream, fileName);

                // Parse mit Mindee Receipt API
                var response = await _mindeeClient.ParseAsync<ReceiptV5>(inputSource);

                if (response?.Document?.Inference?.Prediction == null)
                {
                    _logger.LogWarning("No prediction received from Mindee for file: {FileName}", fileName);
                    return new MindeeReceiptResult { IsSuccess = false, ErrorMessage = "No prediction received" };
                }

                var prediction = response.Document.Inference.Prediction;

                _logger.LogInformation("Successfully parsed receipt from {FileName}. Confidence: {Confidence}",
                    fileName, prediction.TotalAmount?.Confidence ?? 0);

                return MapMindeeToReceiptResult(prediction, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing PDF receipt with Mindee: {FileName}", fileName);
                return new MindeeReceiptResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<MindeeReceiptResult> ParseReceiptFromImageAsync(byte[] imageContent, string fileName)
        {
            try
            {
                _logger.LogInformation("Starting Mindee image parsing for file: {FileName}", fileName);

                using var stream = new MemoryStream(imageContent);
                var inputSource = new LocalInputSource(stream, fileName);

                var response = await _mindeeClient.ParseAsync<ReceiptV5>(inputSource);

                if (response?.Document?.Inference?.Prediction == null)
                {
                    _logger.LogWarning("No prediction received from Mindee for image: {FileName}", fileName);
                    return new MindeeReceiptResult { IsSuccess = false, ErrorMessage = "No prediction received" };
                }

                var prediction = response.Document.Inference.Prediction;

                _logger.LogInformation("Successfully parsed receipt from image {FileName}. Confidence: {Confidence}",
                    fileName, prediction.TotalAmount?.Confidence ?? 0);

                return MapMindeeToReceiptResult(prediction, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing image receipt with Mindee: {FileName}", fileName);
                return new MindeeReceiptResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<bool> IsReceiptDocumentAsync(byte[] content, string contentType)
        {
            try
            {
                // Schnelle Heuristik basierend auf ContentType
                if (contentType?.StartsWith("application/pdf") == true ||
                    contentType?.StartsWith("image/") == true)
                {
                    return true;
                }

                // Bei unsicheren Fällen: Mindee für Klassifikation nutzen
                // (Optional: Kostensparend nur bei Bedarf)
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if document is receipt");
                return false;
            }
        }

        private MindeeReceiptResult MapMindeeToReceiptResult(ReceiptV5Document prediction, string fileName)
        {
            var result = new MindeeReceiptResult
            {
                IsSuccess = true,
                FileName = fileName,
                ExtractedText = ExtractFullText(prediction),
                RawMindeeData = prediction
            };

            // Total Amount
            if (prediction.TotalAmount?.Value != null)
            {
                result.Amount = (decimal)prediction.TotalAmount.Value;
                result.AmountConfidence = prediction.TotalAmount.Confidence ?? 0;
            }

            // Currency (falls verfügbar in neueren Mindee Versionen)
            result.Currency = "EUR"; // Default, kann erweitert werden

            // Date
            if (prediction.Date?.Value != null)
            {
                result.TransactionDate = prediction.Date.DateObject;
                result.DateConfidence = prediction.Date.Confidence ?? 0;
            }

            // Supplier/Merchant
            if (prediction.SupplierName?.Value != null)
            {
                result.Merchant = prediction.SupplierName.Value;
                result.MerchantConfidence = prediction.SupplierName.Confidence ?? 0;
            }

            // Category (basierend auf Merchant oder Line Items)
            result.Category = DetermineCategoryFromMindeeData(prediction);

            // Zusätzliche Details
            result.AdditionalData = ExtractAdditionalData(prediction);

            // Qualitäts-Score berechnen
            result.OverallConfidence = CalculateOverallConfidence(result);

            _logger.LogDebug("Mapped Mindee result: Amount={Amount}, Merchant={Merchant}, Date={Date}, Confidence={Confidence}",
                result.Amount, result.Merchant, result.TransactionDate, result.OverallConfidence);

            return result;
        }

        private string ExtractFullText(ReceiptV5Document prediction)
        {
            var textBuilder = new StringBuilder();

            // Extrahiere alle erkannten Text-Elemente
            if (prediction.LineItems?.Any() == true)
            {
                foreach (var item in prediction.LineItems)
                {
                    if (!string.IsNullOrEmpty(item.Description))
                    {
                        textBuilder.AppendLine(item.Description);
                    }
                    if (item.TotalAmount != null)
                    {
                        textBuilder.AppendLine($"Amount: {item.TotalAmount.Value}");
                    }
                }
            }

            // Zusätzliche Felder
            if (!string.IsNullOrEmpty(prediction.SupplierName?.Value))
            {
                textBuilder.AppendLine($"Supplier: {prediction.SupplierName.Value}");
            }

            if (prediction.TotalAmount?.Value != null)
            {
                textBuilder.AppendLine($"Total: {prediction.TotalAmount.Value}");
            }

            if (prediction.Date?.Value != null)
            {
                textBuilder.AppendLine($"Date: {prediction.Date.Value:yyyy-MM-dd}");
            }

            return textBuilder.ToString();
        }

        private string DetermineCategoryFromMindeeData(ReceiptV5Document prediction)
        {
            var merchant = prediction.SupplierName?.Value?.ToLowerInvariant() ?? "";

            // Kategorisierung basierend auf Merchant
            if (merchant.Contains("rewe") || merchant.Contains("edeka") ||
                merchant.Contains("aldi") || merchant.Contains("lidl") ||
                merchant.Contains("supermarket") || merchant.Contains("grocery"))
                return "Lebensmittel";

            if (merchant.Contains("shell") || merchant.Contains("aral") ||
                merchant.Contains("esso") || merchant.Contains("gas") ||
                merchant.Contains("petrol") || merchant.Contains("fuel"))
                return "Tankstelle";

            if (merchant.Contains("restaurant") || merchant.Contains("café") ||
                merchant.Contains("pizza") || merchant.Contains("burger") ||
                merchant.Contains("food") || merchant.Contains("dining"))
                return "Restaurants";

            if (merchant.Contains("pharmacy") || merchant.Contains("apotheke") ||
                merchant.Contains("dm") || merchant.Contains("rossmann"))
                return "Gesundheit & Drogerie";

            // Kategorisierung basierend auf Line Items
            if (prediction.LineItems?.Any() == true)
            {
                var itemDescriptions = prediction.LineItems
                    .Where(item => !string.IsNullOrEmpty(item.Description))
                    .Select(item => item.Description.ToLowerInvariant())
                    .ToList();

                if (itemDescriptions.Any(desc => desc.Contains("food") || desc.Contains("bread") ||
                                               desc.Contains("milk") || desc.Contains("meat")))
                    return "Lebensmittel";

                if (itemDescriptions.Any(desc => desc.Contains("fuel") || desc.Contains("petrol") ||
                                               desc.Contains("diesel") || desc.Contains("gas")))
                    return "Tankstelle";

                if (itemDescriptions.Any(desc => desc.Contains("clothing") || desc.Contains("shirt") ||
                                               desc.Contains("shoes") || desc.Contains("dress")))
                    return "Kleidung";
            }

            return "Sonstiges";
        }

        private Dictionary<string, object> ExtractAdditionalData(ReceiptV5Document prediction)
        {
            var additionalData = new Dictionary<string, object>();

            // Steuer-Informationen
            if (prediction.TotalTax?.Value != null)
            {
                additionalData["TotalTax"] = prediction.TotalTax.Value;
                additionalData["TaxConfidence"] = prediction.TotalTax.Confidence ?? 0;
            }

            // Trinkgeld
            if (prediction.Tip?.Value != null)
            {
                additionalData["Tip"] = prediction.Tip.Value;
                additionalData["TipConfidence"] = prediction.Tip.Confidence ?? 0;
            }

            // Line Items Details
            if (prediction.LineItems?.Any() == true)
            {
                var lineItems = prediction.LineItems.Select(item => new
                {
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    TotalAmount = item.TotalAmount,
                    Confidence =item.Confidence
                    
                }).ToList();

                additionalData["LineItems"] = lineItems;
                additionalData["LineItemCount"] = lineItems.Count;
            }

            // Supplier Details
            if (!string.IsNullOrEmpty(prediction.SupplierAddress?.Value))
            {
                additionalData["SupplierAddress"] = prediction.SupplierAddress.Value;
            }

            // Zeitstempel der Verarbeitung
            additionalData["ProcessedAt"] = DateTime.UtcNow;
            additionalData["ProcessingMethod"] = "Mindee";

            return additionalData;
        }

        private double CalculateOverallConfidence(MindeeReceiptResult result)
        {
            var confidenceValues = new List<double>();

            if (result.AmountConfidence > 0) confidenceValues.Add(result.AmountConfidence);
            if (result.DateConfidence > 0) confidenceValues.Add(result.DateConfidence);
            if (result.MerchantConfidence > 0) confidenceValues.Add(result.MerchantConfidence);

            return confidenceValues.Any() ? confidenceValues.Average() : 0;
        }
    }

    // Result Models
    public class MindeeReceiptResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;

        // Extracted Data
        public decimal? Amount { get; set; }
        public string Currency { get; set; } = "EUR";
        public DateTime? TransactionDate { get; set; }
        public string Merchant { get; set; } = string.Empty;
        public string Category { get; set; } = "Sonstiges";
        public string ExtractedText { get; set; } = string.Empty;

        // Confidence Scores
        public double AmountConfidence { get; set; }
        public double DateConfidence { get; set; }
        public double MerchantConfidence { get; set; }
        public double OverallConfidence { get; set; }

        // Additional Data
        public Dictionary<string, object> AdditionalData { get; set; } = new();
        public ReceiptV5Document RawMindeeData { get; set; }
    }
}

