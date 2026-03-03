
using System.Windows;
using System;
using System.Linq;
using NexioFinance.Views;
using NexioFinance.Data;    
using NexioFinance.Models;  
namespace NexioFinance
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {

            InitializeComponent();

            InitializeDatabase();
            if (MainContentArea != null)
            {
                MainContentArea.Content = new DashboardView();
            }

        }

        private void Menu_Dashboard_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContentArea != null)
            {
                MainContentArea.Content = new DashboardView();
            }
        }
        private void InitializeDatabase()
        {
            using (var context = new AppDbContext())
            {
                context.Database.EnsureCreated();

                if (!context.Accounts.Any())
                {
                    // ==========================================
                    // 1. VYTVOŘENÍ MĚN (USD je hlavní)
                    // ==========================================
                    context.Currencies.Add(new Currency { Code = "USD", ExchangeRate = 1m, IsMainCurrency = true });
                    context.Currencies.Add(new Currency { Code = "EUR", ExchangeRate = 1.08m, IsMainCurrency = false }); // 1 EUR stojí 1.08 USD
                    context.Currencies.Add(new Currency { Code = "BTC", ExchangeRate = 65000m, IsMainCurrency = false }); // 1 BTC stojí 65 000 USD
                    context.Currencies.Add(new Currency { Code = "ETH", ExchangeRate = 3500m, IsMainCurrency = false }); // 1 ETH stojí 3 500 USD
                    context.SaveChanges();

                    // ==========================================
                    // 2. VYTVOŘENÍ ÚČTŮ (Včetně Krypto peněženek)
                    // ==========================================
                    var mainAccount = new Account { Name = "Chase Checking", Currency = "USD", InitialBalance = 1250 };
                    var savingsAccount = new Account { Name = "BoA Savings", Currency = "USD", InitialBalance = 8500 };
                    var btcAccount = new Account { Name = "Coinbase BTC", Currency = "BTC", InitialBalance = 0.12m };
                    var ethAccount = new Account { Name = "Coinbase ETH", Currency = "ETH", InitialBalance = 1.5m };

                    context.Accounts.AddRange(mainAccount, savingsAccount, btcAccount, ethAccount);
                    context.SaveChanges();

                    // ==========================================
                    // 3. VYTVOŘENÍ BOHATÉHO STROMU KATEGORIÍ
                    // ==========================================
                    var catIncome = new Category { Name = "Income", DefaultType = TransactionType.Income };
                    var catSalary = new Category { Name = "Salary", ParentCategory = catIncome, DefaultType = TransactionType.Income };
                    var catSideHustle = new Category { Name = "Side Hustle", ParentCategory = catIncome, DefaultType = TransactionType.Income };

                    var catHousing = new Category { Name = "Housing", DefaultType = TransactionType.Expense };
                    var catRent = new Category { Name = "Rent", ParentCategory = catHousing, DefaultType = TransactionType.Expense };
                    var catUtilities = new Category { Name = "Utilities", ParentCategory = catHousing, DefaultType = TransactionType.Expense };

                    var catFood = new Category { Name = "Food", DefaultType = TransactionType.Expense };
                    var catGroceries = new Category { Name = "Groceries", ParentCategory = catFood, DefaultType = TransactionType.Expense };
                    var catRestaurants = new Category { Name = "Restaurants", ParentCategory = catFood, DefaultType = TransactionType.Expense };

                    var catTransport = new Category { Name = "Transportation", DefaultType = TransactionType.Expense };
                    var catGas = new Category { Name = "Gas & Fuel", ParentCategory = catTransport, DefaultType = TransactionType.Expense };

                    var catEntertainment = new Category { Name = "Entertainment", DefaultType = TransactionType.Expense };
                    var catSubs = new Category { Name = "Subscriptions", ParentCategory = catEntertainment, DefaultType = TransactionType.Expense };

                    var catShopping = new Category { Name = "Shopping", DefaultType = TransactionType.Expense };
                    var catElectronics = new Category { Name = "Electronics", ParentCategory = catShopping, DefaultType = TransactionType.Expense };

                    var catTransfers = new Category { Name = "Transfers", DefaultType = TransactionType.Transfer };
                    var catSavings = new Category { Name = "Savings", ParentCategory = catTransfers, DefaultType = TransactionType.Transfer };

                    context.Categories.AddRange(
                        catIncome, catSalary, catSideHustle,
                        catHousing, catRent, catUtilities,
                        catFood, catGroceries, catRestaurants,
                        catTransport, catGas,
                        catEntertainment, catSubs,
                        catShopping, catElectronics,
                        catTransfers, catSavings
                    );
                    context.SaveChanges();

                    // ==========================================
                    // 4. GENEROVÁNÍ TRANSAKCÍ (Poslední 3 měsíce)
                    // ==========================================
                    var transactions = new System.Collections.Generic.List<Transaction>();
                    var now = DateTime.Now;

                    // Měsíc -3
                    transactions.Add(new Transaction { Date = now.AddDays(-88), Amount = 4200, Type = TransactionType.Income, Description = "Salary - Tech Corp", AccountId = mainAccount.Id, CategoryId = catSalary.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-87), Amount = -1800, Type = TransactionType.Expense, Description = "Rent Payment", AccountId = mainAccount.Id, CategoryId = catRent.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-85), Amount = -145.20m, Type = TransactionType.Expense, Description = "Walmart Supercenter", AccountId = mainAccount.Id, CategoryId = catGroceries.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-80), Amount = -15.99m, Type = TransactionType.Expense, Description = "Netflix Subscription", AccountId = mainAccount.Id, CategoryId = catSubs.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-78), Amount = -42.50m, Type = TransactionType.Expense, Description = "Shell Gas Station", AccountId = mainAccount.Id, CategoryId = catGas.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-75), Amount = 4200, Type = TransactionType.Income, Description = "Salary - Tech Corp", AccountId = mainAccount.Id, CategoryId = catSalary.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-72), Amount = -299.99m, Type = TransactionType.Expense, Description = "Best Buy - New Monitor", AccountId = mainAccount.Id, CategoryId = catElectronics.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-70), Amount = -65.50m, Type = TransactionType.Expense, Description = "Chili's Grill & Bar", AccountId = mainAccount.Id, CategoryId = catRestaurants.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-62), Amount = -120.00m, Type = TransactionType.Expense, Description = "Electric Bill", AccountId = mainAccount.Id, CategoryId = catUtilities.Id });

                    // Měsíc -2
                    transactions.Add(new Transaction { Date = now.AddDays(-58), Amount = 4200, Type = TransactionType.Income, Description = "Salary - Tech Corp", AccountId = mainAccount.Id, CategoryId = catSalary.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-57), Amount = -1800, Type = TransactionType.Expense, Description = "Rent Payment", AccountId = mainAccount.Id, CategoryId = catRent.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-52), Amount = -110.00m, Type = TransactionType.Expense, Description = "Trader Joe's", AccountId = mainAccount.Id, CategoryId = catGroceries.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-50), Amount = 850, Type = TransactionType.Income, Description = "Upwork - Web Design", AccountId = mainAccount.Id, CategoryId = catSideHustle.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-48), Amount = -15.99m, Type = TransactionType.Expense, Description = "Netflix Subscription", AccountId = mainAccount.Id, CategoryId = catSubs.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-45), Amount = 4200, Type = TransactionType.Income, Description = "Salary - Tech Corp", AccountId = mainAccount.Id, CategoryId = catSalary.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-42), Amount = -125.00m, Type = TransactionType.Expense, Description = "Texas Roadhouse", AccountId = mainAccount.Id, CategoryId = catRestaurants.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-35), Amount = -38.00m, Type = TransactionType.Expense, Description = "Chevron Gas", AccountId = mainAccount.Id, CategoryId = catGas.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-32), Amount = -115.00m, Type = TransactionType.Expense, Description = "Water & Trash Bill", AccountId = mainAccount.Id, CategoryId = catUtilities.Id });

                    // Měsíc -1 (Posledních 30 dní)
                    transactions.Add(new Transaction { Date = now.AddDays(-28), Amount = 4200, Type = TransactionType.Income, Description = "Salary - Tech Corp", AccountId = mainAccount.Id, CategoryId = catSalary.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-27), Amount = -1800, Type = TransactionType.Expense, Description = "Rent Payment", AccountId = mainAccount.Id, CategoryId = catRent.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-24), Amount = -195.30m, Type = TransactionType.Expense, Description = "Whole Foods Market", AccountId = mainAccount.Id, CategoryId = catGroceries.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-20), Amount = -15.99m, Type = TransactionType.Expense, Description = "Netflix Subscription", AccountId = mainAccount.Id, CategoryId = catSubs.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-16), Amount = -210.00m, Type = TransactionType.Expense, Description = "Dinner - The Capital Grille", AccountId = mainAccount.Id, CategoryId = catRestaurants.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-14), Amount = 4200, Type = TransactionType.Income, Description = "Salary - Tech Corp", AccountId = mainAccount.Id, CategoryId = catSalary.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-10), Amount = -45.00m, Type = TransactionType.Expense, Description = "Shell Gas Station", AccountId = mainAccount.Id, CategoryId = catGas.Id });
                    transactions.Add(new Transaction { Date = now.AddDays(-5), Amount = -120.00m, Type = TransactionType.Expense, Description = "Electric Bill", AccountId = mainAccount.Id, CategoryId = catUtilities.Id });

                    // Aktuální/Dnešní transakce
                    transactions.Add(new Transaction { Date = now.AddDays(-1), Amount = 4200, Type = TransactionType.Income, Description = "Salary - Tech Corp", AccountId = mainAccount.Id, CategoryId = catSalary.Id });
                    transactions.Add(new Transaction { Date = now, Amount = -1800, Type = TransactionType.Expense, Description = "Rent Payment", AccountId = mainAccount.Id, CategoryId = catRent.Id });

                    context.Transactions.AddRange(transactions);
                    context.SaveChanges();

                    // ==========================================
                    // 5. UKÁZKOVÝ PŘEVOD (Checking -> Savings)
                    // ==========================================
                    var outTransfer = new Transaction { Date = now.AddDays(-15), Amount = -1000, Type = TransactionType.Transfer, Description = "Transfer to Savings", AccountId = mainAccount.Id, CategoryId = catSavings.Id };
                    var inTransfer = new Transaction { Date = now.AddDays(-15), Amount = 1000, Type = TransactionType.Transfer, Description = "Transfer from Checking", AccountId = savingsAccount.Id, CategoryId = catSavings.Id };

                    context.Transactions.Add(outTransfer);
                    context.Transactions.Add(inTransfer);
                    context.SaveChanges();

                    // Křížové propojení (aby to program bral jako jeden celek)
                    outTransfer.LinkedTransactionId = inTransfer.Id;
                    inTransfer.LinkedTransactionId = outTransfer.Id;
                    context.SaveChanges();

                    MessageBox.Show("Vítejte v Nexio Finance!\n\nUkázková databáze s účty v USD, krypto peněženkami a transakcemi za poslední 3 měsíce byla úspěšně vytvořena. Prozkoumejte záložku Analýza!", "Vítejte", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        private void Menu_Transactions_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContentArea != null)
            {
                MainContentArea.Content = new TransactionsView();
            }
        }
        private void Menu_Analysis_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContentArea != null)
            {
                MainContentArea.Content = new AnalysisView();
            }
        }
        private void Menu_Settings_Checked(object sender, RoutedEventArgs e)
        {
            if (MainContentArea != null)
            {
                MainContentArea.Content = new SettingsView();
            }
        }
    }
}