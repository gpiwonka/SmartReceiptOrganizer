using System.ComponentModel.DataAnnotations;

namespace SmartReceiptOrganizer.Core.Models
{
 
        public class WebhookLog
        {
            public int Id { get; set; }

            [MaxLength(50)]
            public string Source { get; set; } = "Postmark"; // Postmark, Test, etc.

            [MaxLength(100)]
            public string? MessageId { get; set; }

            [MaxLength(500)]
            public string? Subject { get; set; }

            [MaxLength(200)]
            public string? FromEmail { get; set; }

            [MaxLength(100)]
            public string? ToEmail { get; set; }

            [MaxLength(50)]
            public string? ContentType { get; set; }

            public int ContentLength { get; set; }

            [MaxLength(50)]
            public string Status { get; set; } = "Received"; // Received, Processed, Failed

            [MaxLength(1000)]
            public string? ErrorMessage { get; set; }

            public string? RequestHeaders { get; set; } // JSON

            public string? RequestBody { get; set; } // Full JSON body

            public string? ProcessingDetails { get; set; } // JSON mit Details

            public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

            public DateTime? ProcessedAt { get; set; }

            public int ProcessingTimeMs { get; set; }

            public bool HasAttachments { get; set; }

            public int AttachmentCount { get; set; }

            public int? CreatedReceiptId { get; set; }
        }
    }

