// Controllers/ReceiptsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartReceiptOrganizer.Data;
using System.Text.Json;

namespace SmartReceiptOrganizer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReceiptsController : ControllerBase
    {
        private readonly ILogger<ReceiptsController> _logger;
        private readonly ReceiptDbContext _context;

        public ReceiptsController(ILogger<ReceiptsController> logger, ReceiptDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetReceipts([FromQuery] int page = 1, [FromQuery] int size = 20)
        {
            try
            {
                var totalCount = await _context.Receipts.CountAsync();
                var receipts = await _context.Receipts
                    .Include(r => r.Attachments)
                    .OrderByDescending(r => r.ReceivedDate)
                    .Skip((page - 1) * size)
                    .Take(size)
                    .Select(r => new
                    {
                        r.Id,
                        r.EmailId,
                        r.Merchant,
                        r.Amount,
                        r.Currency,
                        r.TransactionDate,
                        r.ReceivedDate,
                        r.Category,
                        r.OriginalEmailSubject,
                        r.IsProcessed,
                        r.CreatedAt,
                        r.UpdatedAt,
                        AttachmentCount = r.Attachments.Count,
                        HasMindeeData = !string.IsNullOrEmpty(r.ExtractedText)
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    totalCount = totalCount,
                    page = page,
                    size = size,
                    totalPages = (int)Math.Ceiling((double)totalCount / size),
                    receipts = receipts,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching receipts");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetReceipt(int id)
        {
            try
            {
                var receipt = await _context.Receipts
                    .Include(r => r.Attachments)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (receipt == null)
                {
                    return NotFound(new { success = false, message = "Receipt not found" });
                }

                // Parse Mindee data if available
                object? mindeeData = null;
                if (!string.IsNullOrEmpty(receipt.ExtractedText))
                {
                    try
                    {
                        mindeeData = JsonSerializer.Deserialize<JsonElement>(receipt.ExtractedText);
                    }
                    catch
                    {
                        // Ignore JSON parsing errors
                    }
                }

                var result = new
                {
                    receipt.Id,
                    receipt.EmailId,
                    receipt.Merchant,
                    receipt.Amount,
                    receipt.Currency,
                    receipt.TransactionDate,
                    receipt.ReceivedDate,
                    receipt.Category,
                    receipt.OriginalEmailSubject,
                    receipt.OriginalEmailBody,
                    receipt.IsProcessed,
                    receipt.CreatedAt,
                    receipt.UpdatedAt,
                    Attachments = receipt.Attachments.Select(a => new
                    {
                        a.Id,
                        a.FileName,
                        a.ContentType,
                        a.FileSize,
                        a.CreatedAt,
                        HasContent = a.Content != null && a.Content.Length > 0,
                        HasProcessingDetails = !string.IsNullOrEmpty(a.ProcessingDetails)
                    }).ToList(),
                    MindeeData = mindeeData
                };

                return Ok(new
                {
                    success = true,
                    receipt = result,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching receipt {ReceiptId}", id);
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("{id}/attachment/{attachmentId}")]
        public async Task<IActionResult> GetAttachment(int id, int attachmentId)
        {
            try
            {
                var attachment = await _context.ReceiptAttachments
                    .FirstOrDefaultAsync(a => a.Id == attachmentId && a.ReceiptId == id);

                if (attachment == null)
                {
                    return NotFound(new { success = false, message = "Attachment not found" });
                }

                if (attachment.Content == null || attachment.Content.Length == 0)
                {
                    return NotFound(new { success = false, message = "Attachment content not available" });
                }

                return File(attachment.Content, attachment.ContentType ?? "application/octet-stream", attachment.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching attachment {AttachmentId} for receipt {ReceiptId}", attachmentId, id);
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var totalReceipts = await _context.Receipts.CountAsync();
                var processedReceipts = await _context.Receipts.CountAsync(r => r.IsProcessed);
                var receiptsWithAmount = await _context.Receipts.CountAsync(r => r.Amount.HasValue);
                var totalAmount = await _context.Receipts
                    .Where(r => r.Amount.HasValue)
                    .SumAsync(r => r.Amount.Value);

                var recentReceipts = await _context.Receipts
                    .Where(r => r.ReceivedDate >= DateTime.UtcNow.AddDays(-30))
                    .CountAsync();

                var topMerchants = await _context.Receipts
                    .Where(r => !string.IsNullOrEmpty(r.Merchant))
                    .GroupBy(r => r.Merchant)
                    .Select(g => new { Merchant = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(10)
                    .ToListAsync();

                var currencyBreakdown = await _context.Receipts
                    .Where(r => !string.IsNullOrEmpty(r.Currency) && r.Amount.HasValue)
                    .GroupBy(r => r.Currency)
                    .Select(g => new {
                        Currency = g.Key,
                        Count = g.Count(),
                        TotalAmount = g.Sum(r => r.Amount.Value)
                    })
                    .OrderByDescending(x => x.TotalAmount)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    stats = new
                    {
                        totalReceipts = totalReceipts,
                        processedReceipts = processedReceipts,
                        receiptsWithAmount = receiptsWithAmount,
                        totalAmount = totalAmount,
                        recentReceipts = recentReceipts,
                        processingRate = totalReceipts > 0 ? Math.Round((double)processedReceipts / totalReceipts * 100, 1) : 0,
                        topMerchants = topMerchants,
                        currencyBreakdown = currencyBreakdown
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching receipt stats");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateReceipt(int id, [FromBody] UpdateReceiptRequest request)
        {
            try
            {
                var receipt = await _context.Receipts.FindAsync(id);
                if (receipt == null)
                {
                    return NotFound(new { success = false, message = "Receipt not found" });
                }

                // Update editable fields
                if (!string.IsNullOrEmpty(request.Merchant))
                    receipt.Merchant = request.Merchant;

                if (request.Amount.HasValue)
                    receipt.Amount = request.Amount;

                if (!string.IsNullOrEmpty(request.Currency))
                    receipt.Currency = request.Currency;

                if (!string.IsNullOrEmpty(request.Category))
                    receipt.Category = request.Category;

                if (request.TransactionDate.HasValue)
                    receipt.TransactionDate = request.TransactionDate;

                receipt.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Receipt {ReceiptId} updated successfully", id);

                return Ok(new
                {
                    success = true,
                    message = "Receipt updated successfully",
                    receiptId = receipt.Id,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating receipt {ReceiptId}", id);
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteReceipt(int id)
        {
            try
            {
                var receipt = await _context.Receipts
                    .Include(r => r.Attachments)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (receipt == null)
                {
                    return NotFound(new { success = false, message = "Receipt not found" });
                }

                _context.Receipts.Remove(receipt);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Receipt {ReceiptId} deleted successfully", id);

                return Ok(new
                {
                    success = true,
                    message = "Receipt deleted successfully",
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting receipt {ReceiptId}", id);
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }
    }

    public class UpdateReceiptRequest
    {
        public string? Merchant { get; set; }
        public decimal? Amount { get; set; }
        public string? Currency { get; set; }
        public string? Category { get; set; }
        public DateTime? TransactionDate { get; set; }
    }
}