using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartReceiptOrganizer.Core.Models.Postmark
{

    public class PostmarkInboundMessage
    {
        [JsonPropertyName("MessageID")]
        public string? MessageId { get; set; }

        [JsonPropertyName("Date")]
        public string? DateString { get; set; } // Parse as STRING first!

        [JsonPropertyName("Subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("From")]
        public string? From { get; set; }

        [JsonPropertyName("FromName")]
        public string? FromName { get; set; }

        [JsonPropertyName("FromFull")]
        public PostmarkEmailAddress? FromFull { get; set; }

        [JsonPropertyName("To")]
        public string? To { get; set; }

        [JsonPropertyName("ToFull")]
        public List<PostmarkEmailAddress>? ToFull { get; set; }

        [JsonPropertyName("TextBody")]
        public string? TextBody { get; set; }

        [JsonPropertyName("HtmlBody")]
        public string? HtmlBody { get; set; }

        [JsonPropertyName("Attachments")]
        public List<PostmarkAttachment>? Attachments { get; set; } = new();

        [JsonPropertyName("Cc")]
        public string? Cc { get; set; }

        [JsonPropertyName("CcFull")]
        public List<PostmarkEmailAddress>? CcFull { get; set; }

        [JsonPropertyName("Bcc")]
        public string? Bcc { get; set; }

        [JsonPropertyName("BccFull")]
        public List<PostmarkEmailAddress>? BccFull { get; set; }

        [JsonPropertyName("OriginalRecipient")]
        public string? OriginalRecipient { get; set; }

        [JsonPropertyName("ReplyTo")]
        public string? ReplyTo { get; set; }

        [JsonPropertyName("MailboxHash")]
        public string? MailboxHash { get; set; }

        [JsonPropertyName("MessageStream")]
        public string? MessageStream { get; set; }

        /// <summary>
        /// Parsed DateTime from DateString - call ParseDate() first!
        /// </summary>
        [JsonIgnore]
        public DateTime Date { get; set; }

        /// <summary>
        /// Parse the DateString into a proper DateTime
        /// Call this after JSON deserialization
        /// </summary>
        public void ParseDate()
        {
            if (string.IsNullOrEmpty(DateString))
            {
                Date = DateTime.UtcNow;
                return;
            }

            var formats = new[]
            {
                "ddd, d MMM yyyy HH:mm:ss zzz",      // "Tue, 3 Jun 2025 21:46:05 +0000"
                "ddd, dd MMM yyyy HH:mm:ss zzz",     // "Tue, 03 Jun 2025 21:46:05 +0000"
                "ddd, d MMM yyyy HH:mm:ss K",        // "Tue, 3 Jun 2025 21:46:05 Z"
                "ddd, dd MMM yyyy HH:mm:ss K",       // "Tue, 03 Jun 2025 21:46:05 Z"
                "yyyy-MM-ddTHH:mm:ssZ",              // ISO 8601 UTC
                "yyyy-MM-ddTHH:mm:ss.fffZ",          // ISO 8601 with milliseconds
                "yyyy-MM-ddTHH:mm:ss",               // ISO 8601 without timezone
                "yyyy-MM-dd HH:mm:ss"                // SQL DateTime format
            };

            foreach (var format in formats)
            {
                if (DateTime.TryParseExact(DateString, format, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var result))
                {
                    Date = result;
                    return;
                }
            }

            // Try general parsing as final fallback
            if (DateTime.TryParse(DateString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var fallbackResult))
            {
                Date = fallbackResult;
                return;
            }

            // Last resort - use current time
            Date = DateTime.UtcNow;
        }
    }

    public class PostmarkEmailAddress
    {
        [JsonPropertyName("Email")]
        public string? Email { get; set; }

        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("MailboxHash")]
        public string? MailboxHash { get; set; }
    }

    public class PostmarkAttachment
    {
        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("Content")]
        public string? Content { get; set; }

        [JsonPropertyName("ContentType")]
        public string? ContentType { get; set; }

        [JsonPropertyName("ContentLength")]
        public int ContentLength { get; set; }

        [JsonPropertyName("ContentID")]
        public string? ContentId { get; set; }
    }
}
