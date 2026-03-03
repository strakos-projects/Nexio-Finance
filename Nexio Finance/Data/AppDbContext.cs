using Microsoft.EntityFrameworkCore;
using NexioFinance.Models;
using System.IO;
using System;

namespace NexioFinance.Data
{
    public class AppDbContext : DbContext
    {
        // Tyto vlastnosti reprezentují samotné tabulky v databázi
        public DbSet<Account> Accounts { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Transaction> Transactions { get; set; }

        public DbSet<Currency> Currencies { get; set; }

        // Nastavení připojení k SQLite
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Databáze se uloží do složky Dokumenty uživatele do složky NexioFinance
            //string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NexioFinance");
            string folder = AppContext.BaseDirectory;
            Directory.CreateDirectory(folder); // Vytvoří složku, pokud neexistuje

            string dbPath = Path.Combine(folder, "nexio_data.db");

            // Říkáme EF Core, že používáme SQLite
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        // Nastavení specifických pravidel pro tvorbu tabulek
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Zabránění smazání nadřazené kategorie, pokud má podkategorie
            modelBuilder.Entity<Category>()
                .HasOne(c => c.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Nastavení pro propojené transakce (Převody)
            modelBuilder.Entity<Transaction>()
                .HasOne(t => t.LinkedTransaction)
                .WithMany()
                .HasForeignKey(t => t.LinkedTransactionId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}