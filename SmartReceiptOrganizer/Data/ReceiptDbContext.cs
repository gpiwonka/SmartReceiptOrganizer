using Microsoft.EntityFrameworkCore;
using SmartReceiptOrganizer.Core.Models;

using System.Collections.Generic;
using System.Reflection.Emit;

namespace SmartReceiptOrganizer.Data
{
    public class ReceiptDbContext : DbContext
    {
        public ReceiptDbContext(DbContextOptions<ReceiptDbContext> options) : base(options)
        {
        }

        public DbSet<Receipt> Receipts { get; set; }
        public DbSet<ReceiptAttachment> ReceiptAttachments { get; set; }
        public DbSet<WebhookLog> WebhookLogs { get; set; } 

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Receipt>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EmailId).HasMaxLength(255);
                entity.Property(e => e.Merchant).HasMaxLength(200);
                entity.Property(e => e.Amount).HasPrecision(18, 2);
                entity.Property(e => e.Currency).HasMaxLength(10);
                entity.Property(e => e.Category).HasMaxLength(100);
                entity.Property(e => e.OriginalEmailSubject).HasMaxLength(500);
                entity.HasIndex(e => e.EmailId).IsUnique();
                entity.HasIndex(e => e.TransactionDate);
                entity.HasIndex(e => e.Category);
            });

            modelBuilder.Entity<ReceiptAttachment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FileName).HasMaxLength(255);
                entity.Property(e => e.ContentType).HasMaxLength(100);
                entity.HasOne(d => d.Receipt)
                      .WithMany(p => p.Attachments)
                      .HasForeignKey(d => d.ReceiptId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}