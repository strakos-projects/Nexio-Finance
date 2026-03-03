using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using NexioFinance.Data;
using NexioFinance.Models;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
namespace NexioFinance.Views
{
    public partial class SettingsView : UserControl
    {
        private Account? _selectedAccount = null;
        private Category? _selectedCategory = null;
        private Currency? _selectedCurrency = null;

        public SettingsView()
        {
            InitializeComponent();
            LoadData();
        }

        // --- SPOLEČNÉ NAČÍTÁNÍ DAT ---
        private void LoadData()
        {
            using (var context = new AppDbContext())
            {
                AccountsListBox.ItemsSource = context.Accounts.ToList();
                var allCurrencies = context.Currencies.ToList();
                var allCategories = context.Categories.ToList();

                CurrenciesListBox.ItemsSource = allCurrencies;
                AccountCurrencyComboBox.ItemsSource = allCurrencies; 
                CategoriesTreeView.ItemsSource = allCategories.Where(c => c.ParentCategoryId == null).ToList();
                ParentCategoryComboBox.ItemsSource = allCategories;

                var mainCurrency = allCurrencies.FirstOrDefault(c => c.IsMainCurrency);
                if (mainCurrency != null)
                {
                    CurrencyRateLabel.Text = $"Kurz vůči {mainCurrency.Code} (např. 25,30)";
                }
                else
                {
                    CurrencyRateLabel.Text = "Kurz vůči hlavní měně";
                }
            }
        }

        // ==========================================================
        // SPRÁVA ÚČTŮ
        // ==========================================================

        private void AccountsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedAccount = AccountsListBox.SelectedItem as Account;
            if (_selectedAccount != null)
            {
                AccountNameTextBox.Text = _selectedAccount.Name;
                AccountBalanceTextBox.Text = _selectedAccount.InitialBalance.ToString();

                AccountCurrencyComboBox.SelectedValue = _selectedAccount.Currency;
            }
        }

        private void NewAccount_Click(object sender, RoutedEventArgs e)
        {
            AccountsListBox.SelectedItem = null;
            _selectedAccount = null;
            AccountNameTextBox.Text = string.Empty;
            AccountBalanceTextBox.Text = "0";
            AccountCurrencyComboBox.SelectedIndex = 0; 
        }

        private void SaveAccount_Click(object sender, RoutedEventArgs e)
        {
            using (var context = new AppDbContext())
            {
                Account accountToSave;

                if (_selectedAccount != null) 
                {
                    accountToSave = context.Accounts.Find(_selectedAccount.Id)!;
                }
                else 
                {
                    accountToSave = new Account();
                    context.Accounts.Add(accountToSave);
                }

                accountToSave.Name = AccountNameTextBox.Text;
                accountToSave.Currency = AccountCurrencyComboBox.SelectedValue?.ToString() ?? "CZK";
                if (decimal.TryParse(AccountBalanceTextBox.Text.Replace(".", ","), out decimal balance))
                {
                    accountToSave.InitialBalance = balance;
                }

                context.SaveChanges();
            }

            LoadData(); // Překreslíme seznam
            MessageBox.Show("Účet úspěšně uložen!", "Úspěch", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==========================================================
        // SPRÁVA KATEGORIÍ
        // ==========================================================

        private void CategoriesTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _selectedCategory = CategoriesTreeView.SelectedItem as Category;
            if (_selectedCategory != null)
            {
                CategoryNameTextBox.Text = _selectedCategory.Name;
                ParentCategoryComboBox.SelectedValue = _selectedCategory.ParentCategoryId;

                if (_selectedCategory.DefaultType == TransactionType.Expense) CategoryTypeComboBox.SelectedIndex = 0;
                else if (_selectedCategory.DefaultType == TransactionType.Income) CategoryTypeComboBox.SelectedIndex = 1;
                else CategoryTypeComboBox.SelectedIndex = 2;
            }
        }

        private void NewCategory_Click(object sender, RoutedEventArgs e)
        {
            _selectedCategory = null;
            CategoryNameTextBox.Text = string.Empty;
            ParentCategoryComboBox.SelectedItem = null;
            CategoryTypeComboBox.SelectedIndex = 0;
        }

        private void SaveCategory_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CategoryNameTextBox.Text))
            {
                MessageBox.Show("Název kategorie nesmí být prázdný.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var context = new AppDbContext())
            {
                Category catToSave;

                if (_selectedCategory != null)
                {
                    catToSave = context.Categories.Find(_selectedCategory.Id)!;
                }
                else 
                {
                    catToSave = new Category();
                    context.Categories.Add(catToSave);
                }

                catToSave.Name = CategoryNameTextBox.Text;

                int? parentId = ParentCategoryComboBox.SelectedValue as int?;
                if (parentId != catToSave.Id)
                {
                    catToSave.ParentCategoryId = parentId;
                }

                if (CategoryTypeComboBox.SelectedIndex == 0) catToSave.DefaultType = TransactionType.Expense;
                else if (CategoryTypeComboBox.SelectedIndex == 1) catToSave.DefaultType = TransactionType.Income;
                else catToSave.DefaultType = TransactionType.Transfer;

                context.SaveChanges();
            }

            LoadData(); // Překreslíme strom
            MessageBox.Show("Kategorie úspěšně uložena!", "Úspěch", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        // ==========================================================
        // METODA PRO SMAZÁNÍ ÚČTU
        // ==========================================================
        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccount == null) return;

            var result = MessageBox.Show(
                $"Opravdu chcete smazat účet '{_selectedAccount.Name}' a všechny transakce na něm?\n\n(Případné příchozí převody z tohoto účtu na jiné vaše účty zůstanou na druhých účtech zachovány.)",
                "Potvrzení smazání účtu", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                using (var context = new AppDbContext())
                {
                    var accToDelete = context.Accounts.Find(_selectedAccount.Id);
                    if (accToDelete != null)
                    {
                       
                        context.Accounts.Remove(accToDelete);
                        context.SaveChanges();
                    }
                }

                NewAccount_Click(null, null); 
                LoadData();                   
                MessageBox.Show("Účet byl úspěšně smazán.", "Úspěch", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }


        // ==========================================================
        // METODA PRO SMAZÁNÍ KATEGORIE
        // ==========================================================
        private void DeleteCategory_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCategory == null) return;

            var result = MessageBox.Show(
                $"Opravdu chcete smazat kategorii '{_selectedCategory.Name}'?\n\nTransakce zařazené v této kategorii NEBUDOU smazány, pouze o tuto kategorii přijdou.",
                "Potvrzení smazání kategorie", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                using (var context = new AppDbContext())
                {
                    var catToDelete = context.Categories.Include(c => c.SubCategories).FirstOrDefault(c => c.Id == _selectedCategory.Id);

                    if (catToDelete != null)
                    {
                        if (catToDelete.SubCategories.Any())
                        {
                            MessageBox.Show("Tuto kategorii nelze smazat, protože obsahuje podkategorie. Nejprve smažte nebo přesuňte její podkategorie.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        var transactionsToUpdate = context.Transactions.Where(t => t.CategoryId == catToDelete.Id).ToList();
                        foreach (var t in transactionsToUpdate)
                        {
                            t.CategoryId = null; 
                        }

                        context.Categories.Remove(catToDelete);
                        context.SaveChanges();
                    }
                }

                NewCategory_Click(null, null);
                LoadData();                   
                MessageBox.Show("Kategorie byla úspěšně smazána a transakce aktualizovány.", "Úspěch", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        // ==========================================================
        // SPRÁVA MĚN
        // ==========================================================

        // Logika pro kliknutí na CheckBox
        private void IsMainCurrencyCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (IsMainCurrencyCheckBox.IsChecked == true)
            {
                CurrencyRateTextBox.Text = "1";
                CurrencyRateTextBox.IsEnabled = false;
            }
            else
            {
                CurrencyRateTextBox.IsEnabled = true; 
            }
        }

        private void CurrenciesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedCurrency = CurrenciesListBox.SelectedItem as Currency;
            if (_selectedCurrency != null)
            {
                CurrencyCodeTextBox.Text = _selectedCurrency.Code;
                CurrencyRateTextBox.Text = _selectedCurrency.ExchangeRate.ToString();

                IsMainCurrencyCheckBox.IsChecked = _selectedCurrency.IsMainCurrency;
                IsMainCurrencyCheckBox_Click(null, null); 
            }
        }

        private void NewCurrency_Click(object sender, RoutedEventArgs e)
        {
            CurrenciesListBox.SelectedItem = null;
            _selectedCurrency = null;
            CurrencyCodeTextBox.Text = string.Empty;

            if (CurrenciesListBox.Items.Count == 0)
            {
                IsMainCurrencyCheckBox.IsChecked = true;
                IsMainCurrencyCheckBox.IsEnabled = false; 
            }
            else
            {
                IsMainCurrencyCheckBox.IsChecked = false;
                IsMainCurrencyCheckBox.IsEnabled = true;
            }

            IsMainCurrencyCheckBox_Click(null, null);
        }

        private void SaveCurrency_Click(object sender, RoutedEventArgs e)
        {
            using (var context = new AppDbContext())
            {
                Currency currencyToSave;

                if (_selectedCurrency != null)
                {
                    currencyToSave = context.Currencies.Find(_selectedCurrency.Id)!;
                }
                else
                {
                    currencyToSave = new Currency();
                    context.Currencies.Add(currencyToSave);
                }

                currencyToSave.Code = CurrencyCodeTextBox.Text.ToUpper();
                bool isMain = IsMainCurrencyCheckBox.IsChecked == true;

                if (isMain)
                {
                    var existingMains = context.Currencies.Where(c => c.IsMainCurrency).ToList();
                    foreach (var c in existingMains) c.IsMainCurrency = false;

                    currencyToSave.ExchangeRate = 1; 
                }
                else
                {
                    if (decimal.TryParse(CurrencyRateTextBox.Text.Replace(".", ","), out decimal rate))
                    {
                        currencyToSave.ExchangeRate = rate;
                    }
                }

                currencyToSave.IsMainCurrency = isMain;
                context.SaveChanges();
            }

            LoadData();
            MessageBox.Show("Měna úspěšně uložena!", "Úspěch", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteCurrency_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedCurrency == null) return;

            if (_selectedCurrency.IsMainCurrency)
            {
                MessageBox.Show("Hlavní měnu nelze smazat. Pokud ji chcete smazat, nastavte nejdříve jinou měnu jako hlavní.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = MessageBox.Show($"Opravdu chcete smazat měnu '{_selectedCurrency.Code}'?", "Potvrzení", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                using (var context = new AppDbContext())
                {
                    var currToDelete = context.Currencies.Find(_selectedCurrency.Id);
                    if (currToDelete != null)
                    {
                        context.Currencies.Remove(currToDelete);
                        context.SaveChanges();
                    }
                }

                NewCurrency_Click(null, null);
                LoadData();
            }
        }
        // ==========================================================
        // EXPORT A IMPORT (JSON)
        // ==========================================================

        private void ExportJson_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"NexioFinance_Data_{DateTime.Now:yyyyMMdd}.json",
                DefaultExt = ".json",
                Filter = "JSON soubory (.json)|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using (var context = new AppDbContext())
                    {
                        var backup = new DatabaseBackup
                        {
                            Currencies = context.Currencies.AsNoTracking().ToList(),
                            Accounts = context.Accounts.AsNoTracking().ToList(),
                            Categories = context.Categories.AsNoTracking().ToList(),
                            Transactions = context.Transactions.AsNoTracking().ToList()
                        };

                        foreach (var c in backup.Categories) { c.ParentCategory = null; c.SubCategories = null; }
                        foreach (var a in backup.Accounts) { a.Transactions = null; }
                        foreach (var t in backup.Transactions) { t.Account = null; t.Category = null; t.LinkedTransaction = null; }

                        var options = new JsonSerializerOptions { WriteIndented = true };
                        string json = JsonSerializer.Serialize(backup, options);

                        File.WriteAllText(dialog.FileName, json);

                        MessageBox.Show("Data byla úspěšně vyexportována!", "Úspěch", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Chyba při exportu dat:\n{ex.Message}", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportJson_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".json",
                Filter = "JSON soubory (.json)|*.json"
            };

            if (dialog.ShowDialog() == true)
            {
                var result = MessageBox.Show(
                    "POZOR! Import z JSON souboru NENÁVRATNĚ SMAŽE všechna vaše aktuální data v aplikaci a nahradí je těmi ze souboru.\n\nOpravdu chcete pokračovat?",
                    "Varování před přepsáním dat",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        string json = File.ReadAllText(dialog.FileName);
                        var backup = JsonSerializer.Deserialize<DatabaseBackup>(json);

                        if (backup != null)
                        {
                            using (var context = new AppDbContext())
                            {
                                context.Database.EnsureDeleted();
                                context.Database.EnsureCreated();

                                if (backup.Currencies != null) context.Currencies.AddRange(backup.Currencies);
                                if (backup.Accounts != null) context.Accounts.AddRange(backup.Accounts);
                                if (backup.Categories != null) context.Categories.AddRange(backup.Categories);

                                if (backup.Transactions != null)
                                {
                                    var linkMemory = new Dictionary<int, int?>();
                                    foreach (var t in backup.Transactions)
                                    {
                                        linkMemory[t.Id] = t.LinkedTransactionId;
                                        t.LinkedTransactionId = null;
                                    }

                                    context.Transactions.AddRange(backup.Transactions);
                                    context.SaveChanges();

                                    foreach (var t in backup.Transactions)
                                    {
                                        t.LinkedTransactionId = linkMemory[t.Id];
                                    }

                                    context.SaveChanges();
                                }
                                else
                                {
                                    context.SaveChanges();
                                }
                            }

                            LoadData();
                            MessageBox.Show("Gratulujeme! Data byla úspěšně importována.", "Úspěch", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Chyba při importu. Zkontrolujte formát souboru.\n{ex.Message}", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
       

        // ==========================================================
        // EXPORT DO EXCELU (CSV)
        // ==========================================================
        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "NexioFinance_Transakce.csv",
                DefaultExt = ".csv",
                Filter = "CSV soubory (.csv)|*.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using (var context = new AppDbContext())
                    {
                        var transactions = context.Transactions
                            .Include(t => t.Account)
                            .Include(t => t.Category)
                            .ThenInclude(c => c.ParentCategory)
                            .OrderBy(t => t.Date)
                            .ToList();

                        var lines = new List<string>();

                        // Standardizovaná hlavička, kterou náš Import očekává
                        lines.Add("Datum;Typ;Částka;Účet;Kategorie;Podkategorie;Popis");

                        foreach (var t in transactions)
                        {
                            string date = t.Date.ToString("dd. MM. yyyy");
                            string type = t.Type == TransactionType.Income ? "Příjem" : (t.Type == TransactionType.Expense ? "Výdaj" : "Převod");
                            string amount = t.Amount.ToString("0.00", new System.Globalization.CultureInfo("cs-CZ"));
                            string acc = EscapeCsv(t.Account?.Name);
                            string cat = EscapeCsv(t.Category?.ParentCategory?.Name ?? t.Category?.Name ?? "");
                            string subcat = t.Category?.ParentCategory != null ? EscapeCsv(t.Category.Name) : "";
                            string desc = EscapeCsv(t.Description);

                            lines.Add($"{date};{type};{amount};{acc};{cat};{subcat};{desc}");
                        }

                        // Uložení s UTF-8 pro zachování háčků a čárek
                        System.IO.File.WriteAllLines(dialog.FileName, lines, System.Text.Encoding.UTF8);
                        MessageBox.Show("Transakce byly úspěšně exportovány do Excelu (CSV).", "Úspěch", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Během exportu došlo k chybě.\n\nDetail: {ex.Message}", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string EscapeCsv(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Contains(";") || text.Contains("\"") || text.Contains("\n") || text.Contains("\r"))
            {
                return $"\"{text.Replace("\"", "\"\"")}\"";
            }
            return text;
        }

        // ==========================================================
        // UNIVERZÁLNÍ IMPORT Z EXCELU (CSV)
        // ==========================================================
        // ==========================================================
        // UNIVERZÁLNÍ IMPORT Z EXCELU (CSV)
        // ==========================================================
        private void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".csv",
                Filter = "CSV soubory (.csv)|*.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                // DOTAZ NA UŽIVATELE: Smazat, nebo přidat?
                var answer = MessageBox.Show(
                    "Chcete před importem smazat všechny stávající transakce v aplikaci?\n\n" +
                    "ANO = Čistý import (vhodné, pokud nahráváte upravený export a nechcete mít data dvakrát).\n" +
                    "NE = Transakce z CSV se pouze přidají k těm současným (vhodné pro každoměsíční import z banky).",
                    "Možnosti importu",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel) return; 

                try
                {
                    using (var context = new AppDbContext())
                    {
                        if (answer == MessageBoxResult.Yes)
                        {
                            context.Transactions.RemoveRange(context.Transactions);
                            context.SaveChanges();
                        }

                        var existingAccounts = context.Accounts.ToList();
                        var existingCategories = context.Categories.ToList();

                        string fullText = System.IO.File.ReadAllText(dialog.FileName, System.Text.Encoding.UTF8);
                        List<string[]> rows = ParseCsv(fullText, ';');

                        int importedCount = 0;
                        var culture = new System.Globalization.CultureInfo("cs-CZ");
                        bool headerFound = false;

                        DateTime lastParsedDate = DateTime.MinValue;
                        int secondsOffset = 0;

                        // Procházíme řádky
                        for (int i = 0; i < rows.Count; i++)
                        {
                            string[] cols = rows[i];

                            if (!headerFound)
                            {
                                if (cols.Length > 0 && cols[0].Trim().Equals("Datum", StringComparison.OrdinalIgnoreCase)) headerFound = true;
                                continue;
                            }

                            if (cols.Length < 4 || string.IsNullOrWhiteSpace(cols[0]) || string.IsNullOrWhiteSpace(cols[3])) continue;

                            // 1. Zpracování Účtu
                            string accName = cols[3].Trim();
                            var account = existingAccounts.FirstOrDefault(a => a.Name.Equals(accName, StringComparison.OrdinalIgnoreCase));
                            if (account == null)
                            {
                                account = new Account { Name = accName, Currency = "CZK", InitialBalance = 0 };
                                context.Accounts.Add(account);
                                context.SaveChanges();
                                existingAccounts.Add(account);
                            }

                            // 2. Zpracování Typu a Částky
                            string typeString = cols[1].Trim();
                            TransactionType type = TransactionType.Expense;
                            if (typeString.Equals("Příjem", StringComparison.OrdinalIgnoreCase)) type = TransactionType.Income;
                            else if (typeString.Equals("Převod", StringComparison.OrdinalIgnoreCase)) type = TransactionType.Transfer;

                            decimal amount = 0;
                            string amountText = cols[2].Replace(" ", "").Replace("\u00A0", "").Replace("\u202F", "");
                            if (amountText.Contains(".") && amountText.Contains(",")) amountText = amountText.Replace(".", "");
                            else if (amountText.Contains(".") && !amountText.Contains(",")) amountText = amountText.Replace(".", ",");
                            decimal.TryParse(amountText, System.Globalization.NumberStyles.Any, culture, out amount);

                            // Vynucení správného znaménka
                            if (type == TransactionType.Expense && amount > 0) amount = -amount;
                            if (type == TransactionType.Income && amount < 0) amount = Math.Abs(amount);

                            // 3. Zpracování Kategorií (Sloupec 4 a 5)
                            string catName = cols.Length > 4 ? cols[4].Trim() : "";
                            string subCatName = cols.Length > 5 ? cols[5].Trim() : "";
                            Category? finalCategory = null;

                            if (!string.IsNullOrWhiteSpace(catName))
                            {
                                var parentCat = existingCategories.FirstOrDefault(c => c.Name.Equals(catName, StringComparison.OrdinalIgnoreCase) && c.ParentCategoryId == null && c.DefaultType == type);
                                if (parentCat == null)
                                {
                                    parentCat = new Category { Name = catName, DefaultType = type };
                                    context.Categories.Add(parentCat);
                                    context.SaveChanges();
                                    existingCategories.Add(parentCat);
                                }
                                finalCategory = parentCat;

                                if (!string.IsNullOrWhiteSpace(subCatName))
                                {
                                    var subCat = existingCategories.FirstOrDefault(c => c.Name.Equals(subCatName, StringComparison.OrdinalIgnoreCase) && c.ParentCategoryId == parentCat.Id);
                                    if (subCat == null)
                                    {
                                        subCat = new Category { Name = subCatName, DefaultType = type, ParentCategoryId = parentCat.Id };
                                        context.Categories.Add(subCat);
                                        context.SaveChanges();
                                        existingCategories.Add(subCat);
                                    }
                                    finalCategory = subCat;
                                }
                            }

                            // 4. Popis (Sloupec 6)
                            string finalDesc = cols.Length > 6 ? cols[6].Trim() : "";

                            // 5. Datum a Zachování pořadí (Posloupnost sekund)
                            DateTime date = DateTime.Now;
                            DateTime.TryParse(cols[0].Trim(), culture, System.Globalization.DateTimeStyles.None, out date);

                            if (date.Date == lastParsedDate.Date) secondsOffset++;
                            else { lastParsedDate = date.Date; secondsOffset = 0; }
                            date = date.AddSeconds(secondsOffset);

                            // 6. Uložení
                            var transaction = new Transaction
                            {
                                AccountId = account.Id,
                                CategoryId = finalCategory?.Id,
                                Type = type,
                                Amount = amount,
                                Date = date,
                                Description = finalDesc
                            };

                            context.Transactions.Add(transaction);
                            importedCount++;
                        }

                        context.SaveChanges();
                        LoadData();
                        MessageBox.Show($"Import z CSV byl úspěšně dokončen!\n\nNaimportováno bylo {importedCount} transakcí.", "Úspěch", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Během importu došlo k chybě.\nZkontrolujte správnou strukturu (sloupce: Datum;Typ;Částka;Účet;Kategorie;Podkategorie;Popis).\n\nDetail: {ex.Message}", "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        // ==========================================================
        // VLASTNÍ CHYTRÁ ČTEČKA CSV
        // Nepřeruší řádek, pokud jsou odřádkování chráněna uvozovkami!
        // ==========================================================
        private List<string[]> ParseCsv(string content, char delimiter)
        {
            var result = new List<string[]>();
            var currentLine = new List<string>();
            var currentValue = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < content.Length && content[i + 1] == '"')
                        {
                            currentValue.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false; 
                        }
                    }
                    else
                    {
                        currentValue.Append(c); 
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == delimiter)
                    {
                        currentLine.Add(currentValue.ToString());
                        currentValue.Clear();
                    }
                    else if (c == '\r')
                    {
                    }
                    else if (c == '\n')
                    {
                        currentLine.Add(currentValue.ToString());
                        result.Add(currentLine.ToArray());
                        currentLine.Clear();
                        currentValue.Clear();
                    }
                    else
                    {
                        currentValue.Append(c);
                    }
                }
            }

            // Uložíme poslední řádek na úplném konci souboru
            if (currentValue.Length > 0 || currentLine.Count > 0)
            {
                currentLine.Add(currentValue.ToString());
                result.Add(currentLine.ToArray());
            }

            return result;
        }
    }
    public class DatabaseBackup
    {
        public List<Currency> Currencies { get; set; } = new List<Currency>();
        public List<Account> Accounts { get; set; } = new List<Account>();
        public List<Category> Categories { get; set; } = new List<Category>();
        public List<Transaction> Transactions { get; set; } = new List<Transaction>();
    }
}