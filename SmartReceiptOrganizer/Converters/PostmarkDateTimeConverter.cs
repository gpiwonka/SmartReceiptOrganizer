using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartReceiptOrganizer.Converters
{
    public class PostmarkDateTimeConverter : JsonConverter<DateTime>
    {
        private static readonly string[] DateFormats = new[]
        {
            "ddd, d MMM yyyy HH:mm:ss zzz",      // "Tue, 3 Jun 2025 21:46:05 +0000"
            "ddd, dd MMM yyyy HH:mm:ss zzz",     // "Tue, 03 Jun 2025 21:46:05 +0000"
            "yyyy-MM-ddTHH:mm:ssZ",              // ISO 8601 UTC
            "yyyy-MM-ddTHH:mm:ss.fffZ",          // ISO 8601 with milliseconds
            "yyyy-MM-ddTHH:mm:ss",               // ISO 8601 without timezone
            "yyyy-MM-dd HH:mm:ss"                // SQL DateTime format
        };

        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (string.IsNullOrEmpty(value))
            {
                return DateTime.UtcNow; // Fallback
            }

            // Try each format
            foreach (var format in DateFormats)
            {
                if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var result))
                {
                    return result;
                }
            }

            // Try general parsing as fallback
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var fallbackResult))
            {
                return fallbackResult;
            }

            // Last resort - return current time
            return DateTime.UtcNow;
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        }
    }
}
