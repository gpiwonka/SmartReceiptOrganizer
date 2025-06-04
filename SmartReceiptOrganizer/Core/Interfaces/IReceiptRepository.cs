using SmartReceiptOrganizer.Core.Models;


namespace SmartReceiptOrganizer.Core.Interfaces
{
    public interface IReceiptRepository
    {
        Task<Receipt> CreateAsync(Receipt receipt);
        Task<Receipt?> GetByIdAsync(int id);
        Task<Receipt?> GetByEmailIdAsync(string emailId);
        Task<List<Receipt>> GetAllAsync();
        Task<List<Receipt>> GetByCategoryAsync(string category);
        Task<List<Receipt>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
        Task<Receipt> UpdateAsync(Receipt receipt);
        Task DeleteAsync(int id);
    }
}
