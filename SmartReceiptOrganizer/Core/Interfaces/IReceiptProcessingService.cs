using SmartReceiptOrganizer.Core.Models.Postmark;

namespace SmartReceiptOrganizer.Core.Interfaces
{
    public interface IReceiptProcessingService
    {
        Task<ReceiptProcessingResult> ProcessInboundEmailAsync(PostmarkInboundMessage message);
    }

    public class ReceiptProcessingResult
    {
        public bool IsSuccess { get; set; }
        public int? ReceiptId { get; set; }
        public string Message { get; set; } = string.Empty; 
        public ExtractedReceiptData ExtractedData { get; set; } = new ExtractedReceiptData();       
        public List<string> Errors { get; set; } = new List<string>();
    }

    public class ExtractedReceiptData
    {
        public string Merchant { get; set; } = string.Empty;    
        public decimal? Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime? TransactionDate { get; set; }
        public string Category { get; set; } = string.Empty;    
        public List<string> DetectedKeywords { get; set; } = new List<string>();
    }
}
