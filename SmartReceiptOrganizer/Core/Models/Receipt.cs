using System.ComponentModel.DataAnnotations;

namespace SmartReceiptOrganizer.Core.Models
{
    public class Receipt
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string EmailId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Merchant { get; set; }

        public decimal? Amount { get; set; }

        [MaxLength(10)]
        public string? Currency { get; set; }

        public DateTime? TransactionDate { get; set; }

        public DateTime ReceivedDate { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string? Category { get; set; }

        [MaxLength(500)]
        public string? OriginalEmailSubject { get; set; }

        public string? OriginalEmailBody { get; set; }

        /// <summary>
        /// Stores the full Mindee API response as JSON
        /// </summary>
        public string? ExtractedText { get; set; }

        public bool IsProcessed { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation property
        public virtual ICollection<ReceiptAttachment> Attachments { get; set; } = new List<ReceiptAttachment>();
    }



    public class ReceiptAttachment
    {
        public int Id { get; set; }

        public int ReceiptId { get; set; }

        [MaxLength(255)]
        public string? FileName { get; set; }

        [MaxLength(100)]
        public string? ContentType { get; set; }

        /// <summary>
        /// Binary content of the attachment (PDF, image, etc.)
        /// </summary>
        public byte[]? Content { get; set; }

        public long? FileSize { get; set; }

        /// <summary>
        /// Stores Mindee processing results as JSON
        /// </summary>
        public string? ProcessingDetails { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public virtual Receipt Receipt { get; set; } = null!;
    }

}