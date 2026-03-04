using Microsoft.EntityFrameworkCore;
using NexioFinance.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NexioFinance.Views
{

    public partial class TransactionsView : UserControl
    {
        private List<TransactionDisplayItem> _allTransactions = new List<TransactionDisplayItem>();

        private bool _isUpdatingFilters = false;

        public TransactionsView()
        {
            InitializeComponent();
            LoadTransactions();
        }

        private void LoadTransactions()
        {
            using (var context = new AppDbContext())
            {
                var transactions = context.Transactions
                    .Include(t => t.Account)
                    .Include(t => t.Category)
                    .ThenInclude(c => c.ParentCategory)
                    .OrderByDescending(t => t.Date)
                    .ToList();

                _allTransactions = transactions.Select(t => new TransactionDisplayItem
                {
                    Id = t.Id,
                    Date = t.Date, 
                    Account = t.Account?.Name ?? "Neznámý účet",
                    AmountColor = t.Amount < 0 ? "#EF4444" : (t.Amount > 0 ? "#10B981" : "#6B7280"),
                    Amount = t.Amount > 0 ? $"+ {t.Amount:N2} {t.Account?.Currency}" : $"{t.Amount:N2} {t.Account?.Currency}",
                    Category = t.Category?.ParentCategory?.Name ?? t.Category?.Name ?? "",
                    Subcategory = t.Category?.ParentCategory != null ? t.Category.Name : "",
                    Description = t.Description ?? ""
                }).ToList();

                PopulateFilters();
                ApplyFilters();
            }
        }

        // ==========================================================
        // MAGIE FILTRŮ
        // ==========================================================

        private void PopulateFilters()
        {
            _isUpdatingFilters = true; 

            string selMonth = MonthFilter.SelectedItem as string ?? "Vše";
            string selYear = YearFilter.SelectedItem as string ?? "Vše";
            string selAcc = AccountFilter.SelectedItem as string ?? "Vše";
            string selCat = CategoryFilter.SelectedItem as string ?? "Vše";
            string selSub = SubcategoryFilter.SelectedItem as string ?? "Vše";

            var months = new List<string> { "Vše", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12" };
            var years = _allTransactions.Select(t => t.Date.Year.ToString()).Distinct().OrderByDescending(y => y).ToList();
            var accounts = _allTransactions.Select(t => t.Account).Where(a => !string.IsNullOrEmpty(a)).Distinct().OrderBy(a => a).ToList();
            var categories = _allTransactions.Select(t => t.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c).ToList();
            var subcategories = _allTransactions.Select(t => t.Subcategory).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToList();

            years.Insert(0, "Vše");
            accounts.Insert(0, "Vše");
            categories.Insert(0, "Vše");
            subcategories.Insert(0, "Vše");

            MonthFilter.ItemsSource = months;
            YearFilter.ItemsSource = years;
            AccountFilter.ItemsSource = accounts;
            CategoryFilter.ItemsSource = categories;
            SubcategoryFilter.ItemsSource = subcategories;

            MonthFilter.SelectedItem = months.Contains(selMonth) ? selMonth : "Vše";
            YearFilter.SelectedItem = years.Contains(selYear) ? selYear : "Vše";
            AccountFilter.SelectedItem = accounts.Contains(selAcc) ? selAcc : "Vše";
            CategoryFilter.SelectedItem = categories.Contains(selCat) ? selCat : "Vše";
            SubcategoryFilter.SelectedItem = subcategories.Contains(selSub) ? selSub : "Vše";

            _isUpdatingFilters = false; 
        }

        private void ApplyFilters()
        {
            if (_isUpdatingFilters) return;

            var filtered = _allTransactions.AsEnumerable();

            string search = SearchTextBox.Text.ToLower();
            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(t =>
                    t.Description.ToLower().Contains(search) ||
                    t.Category.ToLower().Contains(search) ||
                    t.Subcategory.ToLower().Contains(search) ||
                    t.Account.ToLower().Contains(search) ||
                    t.Amount.ToLower().Contains(search)
                );
            }

            if (MonthFilter.SelectedItem is string month && month != "Vše")
                filtered = filtered.Where(t => t.Date.Month.ToString() == month);

            if (YearFilter.SelectedItem is string year && year != "Vše")
                filtered = filtered.Where(t => t.Date.Year.ToString() == year);

            if (AccountFilter.SelectedItem is string acc && acc != "Vše")
                filtered = filtered.Where(t => t.Account == acc);

            if (CategoryFilter.SelectedItem is string cat && cat != "Vše")
                filtered = filtered.Where(t => t.Category == cat);

            if (SubcategoryFilter.SelectedItem is string sub && sub != "Vše")
                filtered = filtered.Where(t => t.Subcategory == sub);

            TransactionsGrid.ItemsSource = filtered.ToList();
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilters();

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _isUpdatingFilters = true;
            SearchTextBox.Text = "";
            MonthFilter.SelectedIndex = 0;
            YearFilter.SelectedIndex = 0;
            AccountFilter.SelectedIndex = 0;
            CategoryFilter.SelectedIndex = 0;
            SubcategoryFilter.SelectedIndex = 0;
            _isUpdatingFilters = false;

            ApplyFilters();
        }

        // ==========================================================
        // TVOJE PŮVODNÍ AKCE PRO DVOJKLIK A PŘIDÁNÍ TRANSAKCE
        // ==========================================================

        private void TransactionsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (TransactionsGrid.SelectedItem is TransactionDisplayItem selectedRow)
            {
                var editWindow = new AddTransactionWindow(selectedRow.Id);
                if (editWindow.ShowDialog() == true) LoadTransactions();
            }
        }

        private void AddTransaction_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var addWindow = new AddTransactionWindow();
            if (addWindow.ShowDialog() == true) LoadTransactions();
        }

        private void EditContext_Click(object sender, RoutedEventArgs e)
        {
            if (TransactionsGrid.SelectedItem is TransactionDisplayItem selectedRow)
            {
                var editWindow = new AddTransactionWindow(selectedRow.Id);
                if (editWindow.ShowDialog() == true) LoadTransactions();
            }
        }
        private void DuplicateContext_Click(object sender, RoutedEventArgs e)
        {
            if (TransactionsGrid.SelectedItem is TransactionDisplayItem selectedRow)
            {
                var duplicateWindow = new AddTransactionWindow(selectedRow.Id, isDuplicate: true);
                if (duplicateWindow.ShowDialog() == true)
                {
                    LoadTransactions();
                }
            }
        }
        private void DeleteContext_Click(object sender, RoutedEventArgs e)
        {
            if (TransactionsGrid.SelectedItem is TransactionDisplayItem selectedRow)
            {
                var result = MessageBox.Show("Opravdu chcete smazat tuto transakci?", "Potvrzení", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    using (var context = new AppDbContext())
                    {
                        var t = context.Transactions.Find(selectedRow.Id);
                        if (t != null)
                        {
                            context.Transactions.Remove(t);
                            context.SaveChanges();
                        }
                    }
                    LoadTransactions();
                }
            }
        }

        public class TransactionDisplayItem
        {
            public DateTime Date { get; set; }
            public string AmountColor { get; set; }
            public int Id { get; set; }
            public string Account { get; set; }
            public string Category { get; set; }
            public string Subcategory { get; set; }
            public string Amount { get; set; }
            public string Description { get; set; }
        }
    }
}