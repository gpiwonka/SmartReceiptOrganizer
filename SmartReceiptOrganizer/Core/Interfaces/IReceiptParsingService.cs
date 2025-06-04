namespace SmartReceiptOrganizer.Core.Interfaces
{
    public interface IReceiptParsingService
    {
        decimal? ExtractAmount(string content);
        string ExtractCurrency(string content);
        DateTime? ExtractDate(string content);
        string ExtractMerchant(string content, string fromEmail);
    }
}