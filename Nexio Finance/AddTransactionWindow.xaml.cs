using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NexioFinance.Data;
using NexioFinance.Models;

namespace NexioFinance.Views
{
    public partial class AddTransactionWindow : Window
    {
        private int? _editingTransactionId = null;

        // Uložíme si účty a měny do paměti, aby okno reagovalo bleskurychle
        private List<Account> _cachedAccounts = new List<Account>();
        private List<Currency> _cachedCurrencies = new List<Currency>();

        public AddTransactionWindow(int? transactionId = null)
        {
            InitializeComponent();
            _editingTransactionId = transactionId;
            LoadDropdownData();

            if (_editingTransactionId.HasValue)
            {
                this.Title = "Úprava transakce";
                LoadTransactionForEdit();
                // Při úpravě existující transakce zakážeme chytrý párový převod (je to příliš složité na editaci, upravuje se každá strana zvlášť)
                IsOwnTransferCheckBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                TransactionDatePicker.SelectedDate = DateTime.Now;
            }
        }

        private void LoadDropdownData()
        {
            using (var context = new AppDbContext())
            {
                _cachedAccounts = context.Accounts.ToList();
                _cachedCurrencies = context.Currencies.ToList();

                AccountComboBox.ItemsSource = _cachedAccounts;
                TargetAccountComboBox.ItemsSource = _cachedAccounts; // Naplníme i cílový účet

                if (_cachedAccounts.Count > 0 && !_editingTransactionId.HasValue)
                    AccountComboBox.SelectedIndex = 0;
            }
            UpdateCategories();
        }

        private void UpdateCategories()
        {
            if (CategoryComboBox == null || TypeComboBox == null) return;

            using (var context = new AppDbContext())
            {
                TransactionType selectedType = TransactionType.Expense;
                if (TypeComboBox.SelectedIndex == 0) selectedType = TransactionType.Expense;
                else if (TypeComboBox.SelectedIndex == 1) selectedType = TransactionType.Income;
                else selectedType = TransactionType.Transfer;

                CategoryComboBox.ItemsSource = context.Categories.Where(c => c.DefaultType == selectedType).ToList();
            }
        }

        private void LoadTransactionForEdit()
        {
            using (var context = new AppDbContext())
            {
                var t = context.Transactions.Find(_editingTransactionId.Value);
                if (t != null)
                {
                    if (t.Type == TransactionType.Expense) TypeComboBox.SelectedIndex = 0;
                    else if (t.Type == TransactionType.Income) TypeComboBox.SelectedIndex = 1;
                    else TypeComboBox.SelectedIndex = 2;

                    TransactionDatePicker.SelectedDate = t.Date;
                    AmountTextBox.Text = Math.Abs(t.Amount).ToString();
                    DescriptionTextBox.Text = t.Description;
                    AccountComboBox.SelectedValue = t.AccountId;
                    CategoryComboBox.SelectedValue = t.CategoryId;
                }
            }
        }

        // =========================================================
        // UX MAGIE: Zobrazování panelů a výpočet kurzů
        // =========================================================

        private void TypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // OCHRANA: Pokud se okno teprve vykresluje a prvky ještě neexistují, přeruš to
            if (IsOwnTransferCheckBox == null || CategoryComboBox == null) return;

            UpdateCategories();

            if (TypeComboBox.SelectedIndex == 2 && !_editingTransactionId.HasValue)
            {
                IsOwnTransferCheckBox.Visibility = Visibility.Visible;
            }
            else
            {
                IsOwnTransferCheckBox.Visibility = Visibility.Collapsed;
                IsOwnTransferCheckBox.IsChecked = false;
                IsOwnTransferCheckBox_Click(null, null);
            }
        }

        private void IsOwnTransferCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (IsOwnTransferCheckBox.IsChecked == true)
            {
                TransferTargetPanel.Visibility = Visibility.Visible;
                CategoryPanel.Visibility = Visibility.Collapsed; // Schováme kategorie
                UpdateExchangeRateUI();
            }
            else
            {
                TransferTargetPanel.Visibility = Visibility.Collapsed;
                CategoryPanel.Visibility = Visibility.Visible; // Vrátíme kategorie
            }
        }

        private void AccountComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateExchangeRateUI();
        private void TargetAccountComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateExchangeRateUI();

        private void UpdateExchangeRateUI()
        {
            // Ochrana před pádem při načítání okna
            if (IsOwnTransferCheckBox?.IsChecked != true || AccountComboBox.SelectedValue == null || TargetAccountComboBox.SelectedValue == null)
                return;

            var sourceAcc = _cachedAccounts.FirstOrDefault(a => a.Id == (int)AccountComboBox.SelectedValue);
            var targetAcc = _cachedAccounts.FirstOrDefault(a => a.Id == (int)TargetAccountComboBox.SelectedValue);

            if (sourceAcc != null && targetAcc != null && sourceAcc.Currency != targetAcc.Currency)
            {
                // Měny jsou různé -> Zobrazíme kurz a matematicky ho předvyplníme!
                ExchangeRatePanel.Visibility = Visibility.Visible;
                ExchangeRateLabel.Text = $"Kurz (1 {sourceAcc.Currency} = ? {targetAcc.Currency})";

                var sourceCurr = _cachedCurrencies.FirstOrDefault(c => c.Code == sourceAcc.Currency);
                var targetCurr = _cachedCurrencies.FirstOrDefault(c => c.Code == targetAcc.Currency);

                if (sourceCurr != null && targetCurr != null && sourceCurr.ExchangeRate != 0)
                {
                    // Vzoreček pro vzájemný kurz (pokud jsou oba vztažené k hlavní měně)
                    decimal suggestedRate = targetCurr.ExchangeRate / sourceCurr.ExchangeRate;
                    ExchangeRateTextBox.Text = suggestedRate.ToString("N4"); // 4 desetinná místa
                }
            }
            else
            {
                // Stejné měny -> Kurz není potřeba
                ExchangeRatePanel.Visibility = Visibility.Collapsed;
            }
        }

        // =========================================================
        // ULOŽENÍ DO DATABÁZE
        // =========================================================

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    // 1. Získáme přesně tu částku, kterou uživatel zadal (včetně případného mínusu)
                    decimal rawAmount = decimal.Parse(AmountTextBox.Text.Replace(".", ","));

                    // 2. Absolutní hodnota (pro interní výpočty vlastních převodů)
                    decimal baseAmount = Math.Abs(rawAmount);

                    if (IsOwnTransferCheckBox.IsChecked == true && !_editingTransactionId.HasValue)
                    {
                        // ----- CHYTRÝ PŘEVOD MEZI VLASTNÍMI ÚČTY -----
                        var sourceAcc = context.Accounts.Find((int)AccountComboBox.SelectedValue);
                        var targetAcc = context.Accounts.Find((int)TargetAccountComboBox.SelectedValue);

                        if (sourceAcc == null || targetAcc == null || sourceAcc.Id == targetAcc.Id)
                        {
                            MessageBox.Show("Vyberte dva různé účty pro převod.", "Chyba", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        decimal targetAmount = baseAmount;

                        // Přepočet podle kurzu, pokud jsou měny různé
                        if (sourceAcc.Currency != targetAcc.Currency)
                        {
                            decimal rate = decimal.Parse(ExchangeRateTextBox.Text.Replace(".", ","));
                            targetAmount = baseAmount * rate;
                        }

                        // Vytvoříme dvě transakce najednou
                        string userDesc = DescriptionTextBox.Text;

                        var outTransaction = new Transaction
                        {
                            Date = TransactionDatePicker.SelectedDate ?? DateTime.Now,
                            Amount = -baseAmount, // Peníze odcházejí
                            Type = TransactionType.Transfer,
                            AccountId = sourceAcc.Id,
                            Description = string.IsNullOrWhiteSpace(userDesc) ? $"Převod na: {targetAcc.Name}" : $"{userDesc} (Převod na: {targetAcc.Name})"
                        };

                        var inTransaction = new Transaction
                        {
                            Date = TransactionDatePicker.SelectedDate ?? DateTime.Now,
                            Amount = targetAmount, // Peníze přicházejí
                            Type = TransactionType.Transfer,
                            AccountId = targetAcc.Id,
                            Description = string.IsNullOrWhiteSpace(userDesc) ? $"Převod z: {sourceAcc.Name}" : $"{userDesc} (Převod z: {sourceAcc.Name})"
                        };

                        // 1. KROK: Přidáme do databáze obě transakce bez propojení
                        context.Transactions.Add(outTransaction);
                        context.Transactions.Add(inTransaction);

                        context.SaveChanges();

                        // 2. KROK: Teď, když už mají ID, je můžeme bezpečně křížem propojit
                        outTransaction.LinkedTransactionId = inTransaction.Id;
                        inTransaction.LinkedTransactionId = outTransaction.Id;
                    }
                    else
                    {
                        // ----- KLASICKÁ TRANSAKCE (Příjem / Výdaj / Cizí převod) -----
                        Transaction transaction;
                        if (_editingTransactionId.HasValue) transaction = context.Transactions.Find(_editingTransactionId.Value)!;
                        else
                        {
                            transaction = new Transaction();
                            context.Transactions.Add(transaction);
                        }

                        transaction.Date = TransactionDatePicker.SelectedDate ?? DateTime.Now;
                        transaction.Description = DescriptionTextBox.Text;
                        transaction.AccountId = (int)AccountComboBox.SelectedValue;

                        if (TypeComboBox.SelectedIndex == 0) // Výdaj
                        {
                            transaction.Type = TransactionType.Expense;
                            transaction.Amount = -baseAmount; // Výdaj je VŽDY mínus
                        }
                        else if (TypeComboBox.SelectedIndex == 1) // Příjem
                        {
                            transaction.Type = TransactionType.Income;
                            transaction.Amount = baseAmount; // Příjem je VŽDY plus
                        }
                        else // Převod mimo vlastní účty
                        {
                            transaction.Type = TransactionType.Transfer;
                            // OPRAVA: Pro cizí převod použijeme přesně to znaménko, jaké jsi napsal do políčka!
                            transaction.Amount = rawAmount;
                        }

                        if (CategoryComboBox.SelectedValue != null && CategoryPanel.Visibility == Visibility.Visible)
                        {
                            transaction.CategoryId = (int)CategoryComboBox.SelectedValue;
                        }
                    }

                    context.SaveChanges();
                }

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Chyba při ukládání. Zkontrolujte, zda je částka a kurz číslo.\n" + ex.Message, "Chyba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}