
using Microsoft.EntityFrameworkCore;
using SmartReceiptOrganizer.Core.Interfaces;
using SmartReceiptOrganizer.Core.Models;
using SmartReceiptOrganizer.Data;


namespace SmartReceiptOrganizer.Services
{
    public class ReceiptRepository : IReceiptRepository
    {
        private readonly ReceiptDbContext _context;

        public ReceiptRepository(ReceiptDbContext context)
        {
            _context = context;
        }

        public async Task<Receipt> CreateAsync(Receipt receipt)
        {
            _context.Receipts.Add(receipt);
            await _context.SaveChangesAsync();
            return receipt;
        }

        public async Task<Receipt?> GetByIdAsync(int id)
        {
            return await _context.Receipts
                .Include(r => r.Attachments)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<Receipt?> GetByEmailIdAsync(string emailId)
        {
            return await _context.Receipts
                .Include(r => r.Attachments)
                .FirstOrDefaultAsync(r => r.EmailId == emailId);
        }

        public async Task<List<Receipt>> GetAllAsync()
        {
            return await _context.Receipts
                .Include(r => r.Attachments)
                .OrderByDescending(r => r.TransactionDate)
                .ToListAsync();
        }

        public async Task<List<Receipt>> GetByCategoryAsync(string category)
        {
            return await _context.Receipts
                .Include(r => r.Attachments)
                .Where(r => r.Category == category)
                .OrderByDescending(r => r.TransactionDate)
                .ToListAsync();
        }

        public async Task<List<Receipt>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            return await _context.Receipts
                .Include(r => r.Attachments)
                .Where(r => r.TransactionDate >= startDate && r.TransactionDate <= endDate)
                .OrderByDescending(r => r.TransactionDate)
                .ToListAsync();
        }

        public async Task<Receipt> UpdateAsync(Receipt receipt)
        {
            _context.Receipts.Update(receipt);
            await _context.SaveChangesAsync();
            return receipt;
        }

        public async Task DeleteAsync(int id)
        {
            var receipt = await _context.Receipts.FindAsync(id);
            if (receipt != null)
            {
                _context.Receipts.Remove(receipt);
                await _context.SaveChangesAsync();
            }
        }
    }
}