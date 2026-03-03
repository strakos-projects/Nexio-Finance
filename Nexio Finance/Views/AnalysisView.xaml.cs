using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using NexioFinance.Data;
using NexioFinance.Models;

namespace NexioFinance.Views
{
    public partial class AnalysisView : UserControl
    {
        private List<Transaction> _allTransactions = new List<Transaction>();
        private List<Currency> _currencies = new List<Currency>();
        private string[] _colors = { "#3B82F6", "#EF4444", "#10B981", "#F59E0B", "#F59E0B", "#8B5CF6", "#EC4899", "#14B8A6", "#6366F1", "#F43F5E", "#0EA5E9", "#84CC16" };

        private int? _drillDownCategoryId = null;

        public AnalysisView()
        {
            InitializeComponent();
            LoadInitialData();
        }

        private void LoadInitialData()
        {
            using (var context = new AppDbContext())
            {
                // DŮLEŽITÉ: Přidáno Include(t => t.Account), abychom věděli měnu transakce
                _allTransactions = context.Transactions
                    .Include(t => t.Account)
                    .Include(t => t.Category)
                    .ThenInclude(c => c.ParentCategory)
                    .Where(t => t.Type != TransactionType.Transfer)
                    .ToList();

                // Načtení a naplnění měn
                _currencies = context.Currencies.ToList();

                // Dočasně odpojíme event, abychom nespouštěli výpočet moc brzo
                CurrencyComboBox.SelectionChanged -= Filter_SelectionChanged;
                CurrencyComboBox.ItemsSource = _currencies;
                var mainCurr = _currencies.FirstOrDefault(c => c.IsMainCurrency) ?? _currencies.FirstOrDefault();
                if (mainCurr != null) CurrencyComboBox.SelectedValue = mainCurr.Code;
                CurrencyComboBox.SelectionChanged += Filter_SelectionChanged;

                var years = _allTransactions.Select(t => t.Date.Year).Distinct().OrderByDescending(y => y).ToList();
                YearComboBox.ItemsSource = years;
                if (years.Count > 0) YearComboBox.SelectedIndex = 0;

                var months = Enumerable.Range(1, 12).ToList();
                MonthComboBox.ItemsSource = months;
                MonthComboBox.SelectedItem = DateTime.Now.Month;
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _drillDownCategoryId = null;
            BackButton.Visibility = Visibility.Collapsed;
            BarChartTitle.Text = "Detail výdajů dle hlavních kategorií";
            UpdateDashboard();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _drillDownCategoryId = null;
            BackButton.Visibility = Visibility.Collapsed;
            BarChartTitle.Text = "Detail výdajů dle hlavních kategorií";
            UpdateDashboard();
        }

        private void UpdateDashboard()
        {
            if (YearComboBox.SelectedItem == null || MonthComboBox.SelectedItem == null || CurrencyComboBox.SelectedItem == null) return;

            int selectedYear = (int)YearComboBox.SelectedItem;
            int selectedMonth = (int)MonthComboBox.SelectedItem;

            // Zjistíme cílovou měnu a její kurz
            var targetCurrency = CurrencyComboBox.SelectedItem as Currency;
            decimal targetRate = targetCurrency.ExchangeRate != 0 ? targetCurrency.ExchangeRate : 1m;

            var filtered = _allTransactions.Where(t => t.Date.Year == selectedYear && t.Date.Month == selectedMonth).ToList();

            // KOUZLO: Převedeme všechny filtrované transakce do společné zvolené měny!
            var convertedData = new List<ChartTransactionItem>();
            foreach (var t in filtered)
            {
                // 1. Zjistíme kurz účtu, na kterém transakce proběhla
                var accCurrency = _currencies.FirstOrDefault(c => c.Code == t.Account.Currency);
                decimal rateToMain = (accCurrency != null && accCurrency.ExchangeRate != 0) ? accCurrency.ExchangeRate : 1m;

                // 2. Přepočet: Hodnota * Kurz Účtu / Kurz Cílové Měny
                decimal amountInMain = t.Amount * rateToMain;
                decimal finalAmount = amountInMain / targetRate;

                convertedData.Add(new ChartTransactionItem
                {
                    Category = t.Category,
                    Type = t.Type,
                    ConvertedAmount = finalAmount
                });
            }

            // Sčítáme už převedené částky
            decimal totalInc = convertedData.Where(t => t.Type == TransactionType.Income).Sum(t => t.ConvertedAmount);
            decimal totalExp = Math.Abs(convertedData.Where(t => t.Type == TransactionType.Expense).Sum(t => t.ConvertedAmount));

            TotalIncomeText.Text = $"{totalInc:N2} {targetCurrency.Code}";
            TotalExpenseText.Text = $"{totalExp:N2} {targetCurrency.Code}";
            SavingsText.Text = $"{(totalInc - totalExp):N2} {targetCurrency.Code}";

            // Grafům už pošleme převedená data
            DrawIncomePieChart(convertedData);
            DrawExpensePieChart(convertedData);
            DrawBarChart(convertedData);

            // NOVÝ GRAF: Pošleme mu cílovou měnu a kurz, ať může přepočítat i 10 let stará data!
            DrawIncomeTrendChart(targetCurrency, targetRate);
        }
        // ==========================================
        // VÝVOJ PŘÍJMŮ (Všechny měsíce v historii)
        // ==========================================
        private void DrawIncomeTrendChart(Currency targetCurrency, decimal targetRate)
        {
            IncomeTrendCanvas.Children.Clear();

            // 1. Vezmeme ÚPLNĚ VŠECHNY příjmy z databáze (ignorujeme měsíc vybraný v roletce)
            var allIncomes = _allTransactions.Where(t => t.Type == TransactionType.Income).ToList();
            if (!allIncomes.Any()) return;

            // 2. Seskupíme je podle ROKU a MĚSÍCE a rovnou je i matematicky převedeme do vybrané měny
            var grouped = allIncomes
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Amount = g.Sum(t =>
                    {
                        var accCurrency = _currencies.FirstOrDefault(c => c.Code == t.Account.Currency);
                        decimal rateToMain = (accCurrency != null && accCurrency.ExchangeRate != 0) ? accCurrency.ExchangeRate : 1m;
                        return (t.Amount * rateToMain) / targetRate;
                    })
                })
                .OrderBy(x => x.Year).ThenBy(x => x.Month) // Zaručíme, že jdou chronologicky za sebou
                .ToList();

            if (!grouped.Any()) return;

            // 3. Vykreslení grafu
            double canvasHeight = 140;
            double maxAmount = (double)grouped.Max(g => g.Amount);
            if (maxAmount == 0) maxAmount = 1;

            double barWidth = 45;
            double spacing = 30;
            double currentX = 20;

            // Dynamicky roztáhneme plátno podle toho, kolik měsíců vykreslujeme (aby fungoval scrollbar!)
            IncomeTrendCanvas.Width = grouped.Count * (barWidth + spacing) + 40;

            for (int i = 0; i < grouped.Count; i++)
            {
                var item = grouped[i];
                double barHeight = ((double)item.Amount / maxAmount) * canvasHeight;
                if (barHeight < 5) barHeight = 5;

                // Samotný sloupec
                Rectangle bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")), // Zelená barva pro příjmy
                    RadiusX = 4,
                    RadiusY = 4
                };

                Canvas.SetLeft(bar, currentX);
                Canvas.SetBottom(bar, 25);
                IncomeTrendCanvas.Children.Add(bar);

                // Částka nad sloupcem
                TextBlock amountText = new TextBlock
                {
                    Text = $"{item.Amount:N0}",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.DimGray,
                    Width = barWidth + 20,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(amountText, currentX - 10);
                Canvas.SetBottom(amountText, 25 + barHeight + 5);
                IncomeTrendCanvas.Children.Add(amountText);

                // Datum pod sloupcem (Měsíc/Rok)
                TextBlock dateText = new TextBlock
                {
                    Text = $"{item.Month}/{item.Year}",
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    Width = barWidth + 20,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(dateText, currentX - 10);
                Canvas.SetBottom(dateText, 5);
                IncomeTrendCanvas.Children.Add(dateText);

                currentX += barWidth + spacing;
            }
        }
        // ==========================================
        // VYKRESLOVÁNÍ KOLÁČŮ (MATEMATIKA)
        // ==========================================
        private void DrawIncomePieChart(List<ChartTransactionItem> data)
        {
            var grouped = data.Where(t => t.Type == TransactionType.Income && t.Category != null)
                .GroupBy(t => t.Category.ParentCategory?.Name ?? t.Category.Name)
                .Select(g => new { Name = g.Key, Amount = g.Sum(t => t.ConvertedAmount) })
                .Where(x => x.Amount > 0)
                .OrderByDescending(x => x.Amount).ToList();

            DrawPie(IncomePieCanvas, IncomeLegend, grouped.Select(x => x.Name).ToList(), grouped.Select(x => (double)x.Amount).ToList());
        }

        private void DrawExpensePieChart(List<ChartTransactionItem> data)
        {
            var grouped = data.Where(t => t.Type == TransactionType.Expense && t.Category != null)
                .GroupBy(t => t.Category.ParentCategory?.Name ?? t.Category.Name)
                .Select(g => new { Name = g.Key, Amount = Math.Abs(g.Sum(t => t.ConvertedAmount)) })
                .Where(x => x.Amount > 0)
                .OrderByDescending(x => x.Amount).ToList();

            DrawPie(ExpensePieCanvas, ExpenseLegend, grouped.Select(x => x.Name).ToList(), grouped.Select(x => (double)x.Amount).ToList());
        }

        private void DrawPie(Canvas canvas, StackPanel legend, List<string> names, List<double> amounts)
        {
            canvas.Children.Clear();
            legend.Children.Clear();

            double total = amounts.Sum();
            if (total == 0) return;

            double radius = 90;
            Point center = new Point(radius, radius);
            double currentAngle = -Math.PI / 2;

            for (int i = 0; i < amounts.Count; i++)
            {
                double share = amounts[i] / total;
                Brush color = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_colors[i % _colors.Length]));

                if (share >= 0.999)
                {
                    Ellipse circle = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = color };
                    canvas.Children.Add(circle);
                }
                else
                {
                    double sweepAngle = share * 2 * Math.PI;
                    Point startPoint = new Point(center.X + radius * Math.Cos(currentAngle), center.Y + radius * Math.Sin(currentAngle));

                    currentAngle += sweepAngle;
                    Point endPoint = new Point(center.X + radius * Math.Cos(currentAngle), center.Y + radius * Math.Sin(currentAngle));

                    PathGeometry geometry = new PathGeometry();
                    PathFigure figure = new PathFigure { StartPoint = center, IsClosed = true };
                    figure.Segments.Add(new LineSegment(startPoint, false));
                    figure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius), 0, share > 0.5, SweepDirection.Clockwise, false));

                    geometry.Figures.Add(figure);
                    canvas.Children.Add(new Path { Fill = color, Data = geometry });
                }

                StackPanel legendItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
                legendItem.Children.Add(new Rectangle { Width = 12, Height = 12, Fill = color, Margin = new Thickness(0, 0, 5, 0), RadiusX = 2, RadiusY = 2 });
                legendItem.Children.Add(new TextBlock { Text = $"{names[i]} ({share:P1})", FontSize = 12, Foreground = Brushes.DarkGray });
                legend.Children.Add(legendItem);
            }
        }

        // ==========================================
        // VYKRESLOVÁNÍ SLOUPCŮ A DRILL-DOWN ROZKLIK
        // ==========================================
        private void DrawBarChart(List<ChartTransactionItem> data)
        {
            ExpenseBarCanvas.Children.Clear();

            var relevantData = data.Where(t => t.Type == TransactionType.Expense && t.Category != null).ToList();

            var grouped = new List<CategoryChartItem>();

            if (_drillDownCategoryId == null)
            {
                grouped = relevantData
                    .GroupBy(t => t.Category.ParentCategory ?? t.Category)
                    .Select(g => new CategoryChartItem { Id = g.Key.Id, Name = g.Key.Name, Amount = Math.Abs(g.Sum(t => t.ConvertedAmount)) })
                    .Where(x => x.Amount > 0)
                    .OrderByDescending(x => x.Amount).ToList();
            }
            else
            {
                grouped = relevantData
                    .Where(t => t.Category.ParentCategoryId == _drillDownCategoryId || t.Category.Id == _drillDownCategoryId)
                    .GroupBy(t => t.Category)
                    .Select(g => new CategoryChartItem { Id = g.Key.Id, Name = g.Key.Name, Amount = Math.Abs(g.Sum(t => t.ConvertedAmount)) })
                    .Where(x => x.Amount > 0)
                    .OrderByDescending(x => x.Amount).ToList();
            }

            if (grouped.Count == 0) return;

            double canvasHeight = 200;
            double maxAmount = (double)grouped.Max(g => g.Amount);
            double barWidth = 60;
            double spacing = 40;
            double currentX = 20;

            for (int i = 0; i < grouped.Count; i++)
            {
                var item = grouped[i];
                double barHeight = ((double)item.Amount / maxAmount) * canvasHeight;
                if (barHeight < 5) barHeight = 5;

                Rectangle bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_colors[i % _colors.Length])),
                    RadiusX = 4,
                    RadiusY = 4,
                    Cursor = _drillDownCategoryId == null ? Cursors.Hand : Cursors.Arrow,
                    Tag = item.Id
                };

                Canvas.SetLeft(bar, currentX);
                Canvas.SetBottom(bar, 30);

                if (_drillDownCategoryId == null)
                {
                    bar.MouseLeftButtonUp += (s, e) =>
                    {
                        _drillDownCategoryId = (int)((Rectangle)s).Tag;
                        BarChartTitle.Text = $"Detail podkategorií: {item.Name}";
                        BackButton.Visibility = Visibility.Visible;
                        UpdateDashboard();
                    };
                }
                ExpenseBarCanvas.Children.Add(bar);

                TextBlock amountText = new TextBlock
                {
                    Text = $"{item.Amount:N0}",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.DimGray,
                    Width = barWidth + 20,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(amountText, currentX - 10);
                Canvas.SetBottom(amountText, 30 + barHeight + 5);
                ExpenseBarCanvas.Children.Add(amountText);

                TextBlock nameText = new TextBlock
                {
                    Text = item.Name.Length > 10 ? item.Name.Substring(0, 8) + ".." : item.Name,
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    Width = barWidth + 20,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(nameText, currentX - 10);
                Canvas.SetBottom(nameText, 5);
                ExpenseBarCanvas.Children.Add(nameText);

                currentX += barWidth + spacing;
            }
        }
    }

    // Pomocná třída pro sjednocení měn v grafech
    public class ChartTransactionItem
    {
        public Category Category { get; set; }
        public TransactionType Type { get; set; }
        public decimal ConvertedAmount { get; set; }
    }

    // Pomocná třída pro sloupcový graf
    public class CategoryChartItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
    }
}