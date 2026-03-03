using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using NexioFinance.Data;
using NexioFinance.Models; // Důležité: Přidáno pro práci s modelem Currency

namespace NexioFinance.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            this.Loaded += DashboardView_Loaded;
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            // Tento kód se spustí vždy, když se na Dashboard překlikneš
            using (var context = new AppDbContext())
            {
                var currencies = context.Currencies.ToList();

                // Zapamatujeme si aktuálně vybranou měnu (pokud nějaká je)
                string selectedCode = TargetCurrencyComboBox.SelectedValue?.ToString() ?? "";

                // Na chvíli odpojíme event, abychom při plnění roletky nespustili výpočet dřív, než chceme
                TargetCurrencyComboBox.SelectionChanged -= TargetCurrencyComboBox_SelectionChanged;

                // Naplníme roletku dynamicky z databáze
                TargetCurrencyComboBox.ItemsSource = currencies;

                if (!string.IsNullOrEmpty(selectedCode) && currencies.Any(c => c.Code == selectedCode))
                {
                    // Udržíme výběr měny, kterou uživatel už měl zakliknutou
                    TargetCurrencyComboBox.SelectedValue = selectedCode;
                }
                else
                {
                    // Jinak automaticky vybereme tu Měnu, která je v databázi označená jako Hlavní
                    var mainCurr = currencies.FirstOrDefault(c => c.IsMainCurrency) ?? currencies.FirstOrDefault();
                    if (mainCurr != null)
                    {
                        TargetCurrencyComboBox.SelectedValue = mainCurr.Code;
                    }
                }

                // Znovu připojíme event a spustíme výpočet jmění
                TargetCurrencyComboBox.SelectionChanged += TargetCurrencyComboBox_SelectionChanged;
            }

            LoadDashboardData();
        }

        private void TargetCurrencyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.IsLoaded)
            {
                LoadDashboardData();
            }
        }

        private void LoadDashboardData()
        {
            var targetCurrency = TargetCurrencyComboBox.SelectedItem as Currency;
            if (targetCurrency == null) return;

            using (var context = new AppDbContext())
            {
                var currencies = context.Currencies.ToList();
                var accounts = context.Accounts.Include(a => a.Transactions).ToList();

                decimal totalNetWorthMain = 0;
                var displayAccounts = new List<AccountDisplayItem>();

                foreach (var acc in accounts)
                {
                    decimal currentBalance = acc.InitialBalance + acc.Transactions.Sum(t => t.Amount);

                    displayAccounts.Add(new AccountDisplayItem
                    {
                        Name = acc.Name,
                        Currency = acc.Currency,
                        BalanceFormatted = $"{currentBalance:N2}"
                    });

                    var accCurrency = currencies.FirstOrDefault(c => c.Code == acc.Currency);
                    decimal rateToMain = (accCurrency != null && accCurrency.ExchangeRate != 0) ? accCurrency.ExchangeRate : 1m;

                    // OPRAVA 1: Z cizího účtu do celkových CZK musíme NÁSOBIT
                    // (např. účet 10 EUR * kurz 24.25 = 242.50 CZK)
                    totalNetWorthMain += (currentBalance * rateToMain);
                }

                // OPRAVA 2: Z celkových CZK do zobrazení v roletce (EUR) musíme DĚLIT
                // (např. celkem máme 520 CZK / kurz 24.25 = 21.44 EUR)
                decimal targetRate = targetCurrency.ExchangeRate != 0 ? targetCurrency.ExchangeRate : 1m;
                decimal finalNetWorth = totalNetWorthMain / targetRate;

                // Vykreslení
                AccountsItemsControl.ItemsSource = displayAccounts;
                TotalNetWorthTextBlock.Text = $"{finalNetWorth:N2} {targetCurrency.Code}";
            }
        }
    }

    public class AccountDisplayItem
    {
        public string Name { get; set; }
        public string Currency { get; set; }
        public string BalanceFormatted { get; set; }
    }
}