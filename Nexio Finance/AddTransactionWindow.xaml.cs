using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexio_Finance.Properties;
using NexioFinance.Data;
using NexioFinance.Models;

namespace NexioFinance.Views
{
    public partial class AddTransactionWindow : Window
    {
        private int? _editingTransactionId = null;

        private List<Account> _cachedAccounts = new List<Account>();
        private List<Currency> _cachedCurrencies = new List<Currency>();

        public AddTransactionWindow(int? transactionId = null, bool isDuplicate = false)
        {
            InitializeComponent();
            _editingTransactionId = transactionId;
            LoadDropdownData();

            if (_editingTransactionId.HasValue)
            {
                LoadTransactionForEdit();

                if (isDuplicate)
                {
                    _editingTransactionId = null;
                    TransactionDatePicker.SelectedDate = DateTime.Now;
                    IsOwnTransferCheckBox.Visibility = Visibility.Collapsed;

                    ApplyWindowModeTheme(isEditMode: false, isDuplicateMode: true);
                }
                else
                {
                    IsOwnTransferCheckBox.Visibility = Visibility.Collapsed;
                    ApplyWindowModeTheme(isEditMode: true, isDuplicateMode: false);
                }
            }
            else
            {
                TransactionDatePicker.SelectedDate = DateTime.Now;
                ApplyWindowModeTheme(isEditMode: false, isDuplicateMode: false);
            }

            UpdateAmountSignUI();
        }

        private void ApplyWindowModeTheme(bool isEditMode, bool isDuplicateMode)
        {
            var bc = new BrushConverter();

            if (isEditMode)
            {
                // Žluté / jantarové téma pro úpravu existujícího záznamu
                this.Title = Nexio_Finance.Properties.Resources.WinEditTransactionTitle;
                HeaderTitleText.Text = Nexio_Finance.Properties.Resources.EditRecord;
                HeaderIconText.Text = "✎";
                SaveButton.Content = Nexio_Finance.Properties.Resources.BtnSaveChanges;

                this.Background = (Brush)bc.ConvertFrom("#FEF9C3")!;
                HeaderBanner.Background = (Brush)bc.ConvertFrom("#FEF08A")!;
                HeaderBanner.BorderBrush = (Brush)bc.ConvertFrom("#F59E0B")!;
                HeaderIconBadge.Background = (Brush)bc.ConvertFrom("#D97706")!;
                HeaderTitleText.Foreground = (Brush)bc.ConvertFrom("#92400E")!;
                FormCardBorder.BorderBrush = (Brush)bc.ConvertFrom("#FBBF24")!;
                SaveButton.Background = (Brush)bc.ConvertFrom("#D97706")!;
            }
            else
            {
                // Modré téma pro přidání nového záznamu nebo kopii
                this.Title = isDuplicateMode
                    ? Nexio_Finance.Properties.Resources.WinDuplicateTransactionTitle
                    : Nexio_Finance.Properties.Resources.WinAddTransactionTitle;

                HeaderTitleText.Text = isDuplicateMode
                    ? Nexio_Finance.Properties.Resources.DuplicateRecord
                    : Nexio_Finance.Properties.Resources.AddRecord;

                HeaderIconText.Text = isDuplicateMode ? "❐" : "＋";
                SaveButton.Content = Nexio_Finance.Properties.Resources.BtnSaveTransaction;

                this.Background = (Brush)bc.ConvertFrom("#EFF6FF")!;
                HeaderBanner.Background = (Brush)bc.ConvertFrom("#DBEAFE")!;
                HeaderBanner.BorderBrush = (Brush)bc.ConvertFrom("#93C5FD")!;
                HeaderIconBadge.Background = (Brush)bc.ConvertFrom("#2563EB")!;
                HeaderTitleText.Foreground = (Brush)bc.ConvertFrom("#1E3A8A")!;
                FormCardBorder.BorderBrush = (Brush)bc.ConvertFrom("#BFDBFE")!;
                SaveButton.Background = (Brush)bc.ConvertFrom("#2563EB")!;
            }
        }

        private void UpdateAmountSignUI()
        {
            if (ExpenseMinusBadge == null || TypeComboBox == null) return;

            if (TypeComboBox.SelectedIndex == 0) // Výdaj -> zobrazíme pevné designové "−"
            {
                ExpenseMinusBadge.Visibility = Visibility.Visible;

                // Pokud uživatel přepnul z Převodu, kde měl napsané "-", odstraníme ho, ať v poli nejsou dvě mínus
                if (!string.IsNullOrWhiteSpace(AmountTextBox?.Text) && AmountTextBox.Text.TrimStart().StartsWith("-"))
                {
                    AmountTextBox.Text = AmountTextBox.Text.TrimStart().TrimStart('-').TrimStart();
                }
            }
            else if (TypeComboBox.SelectedIndex == 1) // Příjem -> bez pevného mínusu, kladné číslo
            {
                ExpenseMinusBadge.Visibility = Visibility.Collapsed;

                if (!string.IsNullOrWhiteSpace(AmountTextBox?.Text) && AmountTextBox.Text.TrimStart().StartsWith("-"))
                {
                    AmountTextBox.Text = AmountTextBox.Text.TrimStart().TrimStart('-').TrimStart();
                }
            }
            else // Převod -> uživatel zadává znaménko sám (+ nebo -)
            {
                ExpenseMinusBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadDropdownData()
        {
            using (var context = new AppDbContext())
            {
                _cachedAccounts = context.Accounts.ToList();
                _cachedCurrencies = context.Currencies.ToList();

                AccountComboBox.ItemsSource = _cachedAccounts;
                TargetAccountComboBox.ItemsSource = _cachedAccounts;

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

                    // U převodů (Transfer) musíme zachovat původní znaménko (+ nebo -),
                    // aby se odchozí převod při uložení nezměnil na příchozí!
                    if (t.Type == TransactionType.Transfer)
                    {
                        AmountTextBox.Text = t.Amount.ToString();
                    }
                    else
                    {
                        AmountTextBox.Text = Math.Abs(t.Amount).ToString();
                    }

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
            if (IsOwnTransferCheckBox == null || CategoryComboBox == null) return;

            UpdateCategories();
            UpdateAmountSignUI();

            if (TypeComboBox.SelectedIndex == 2 && !_editingTransactionId.HasValue)
            {
                IsOwnTransferCheckBox.Visibility = Visibility.Visible;
            }
            else
            {
                IsOwnTransferCheckBox.Visibility = Visibility.Collapsed;
                IsOwnTransferCheckBox.IsChecked = false;
                IsOwnTransferCheckBox_Click(null!, null!);
            }
        }

        private void IsOwnTransferCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (IsOwnTransferCheckBox.IsChecked == true)
            {
                TransferTargetPanel.Visibility = Visibility.Visible;
                CategoryPanel.Visibility = Visibility.Collapsed;
                UpdateExchangeRateUI();
            }
            else
            {
                TransferTargetPanel.Visibility = Visibility.Collapsed;
                CategoryPanel.Visibility = Visibility.Visible;
            }
        }

        private void AccountComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateExchangeRateUI();
        private void TargetAccountComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateExchangeRateUI();

        private void UpdateExchangeRateUI()
        {
            if (IsOwnTransferCheckBox?.IsChecked != true || AccountComboBox.SelectedValue == null || TargetAccountComboBox.SelectedValue == null)
                return;

            var sourceAcc = _cachedAccounts.FirstOrDefault(a => a.Id == (int)AccountComboBox.SelectedValue);
            var targetAcc = _cachedAccounts.FirstOrDefault(a => a.Id == (int)TargetAccountComboBox.SelectedValue);

            if (sourceAcc != null && targetAcc != null && sourceAcc.Currency != targetAcc.Currency)
            {
                ExchangeRatePanel.Visibility = Visibility.Visible;
                ExchangeRateLabel.Text = $"Kurz (1 {sourceAcc.Currency} = ? {targetAcc.Currency})";

                var sourceCurr = _cachedCurrencies.FirstOrDefault(c => c.Code == sourceAcc.Currency);
                var targetCurr = _cachedCurrencies.FirstOrDefault(c => c.Code == targetAcc.Currency);

                if (sourceCurr != null && targetCurr != null && sourceCurr.ExchangeRate != 0)
                {
                    decimal suggestedRate = targetCurr.ExchangeRate / sourceCurr.ExchangeRate;
                    ExchangeRateTextBox.Text = suggestedRate.ToString("N4");
                }
            }
            else
            {
                ExchangeRatePanel.Visibility = Visibility.Collapsed;
            }
        }

        // =========================================================
        // ULOŽENÍ DO DATABÁZE
        // =========================================================

        private static decimal ParseDecimalFlexible(string input)
        {
            string normalized = (input ?? string.Empty).Trim().Replace(" ", "").Replace(",", ".");
            return decimal.Parse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    // 1. Získáme přesně tu částku, kterou uživatel zadal (včetně případného mínusu)
                    decimal rawAmount = ParseDecimalFlexible(AmountTextBox.Text);

                    // 2. Absolutní hodnota (pro výdaje, příjmy a interní převody)
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

                        if (sourceAcc.Currency != targetAcc.Currency)
                        {
                            decimal rate = ParseDecimalFlexible(ExchangeRateTextBox.Text);
                            targetAmount = baseAmount * rate;
                        }

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

                        context.Transactions.Add(outTransaction);
                        context.Transactions.Add(inTransaction);

                        context.SaveChanges();

                        outTransaction.LinkedTransactionId = inTransaction.Id;
                        inTransaction.LinkedTransactionId = outTransaction.Id;
                    }
                    else
                    {
                        // ----- KLASICKÁ TRANSAKCE (Příjem / Výdaj / Převod) -----
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
                        else // Převod
                        {
                            transaction.Type = TransactionType.Transfer;
                            // Zachová přesné znaménko (+ nebo -) načtené nebo zadané v poli
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