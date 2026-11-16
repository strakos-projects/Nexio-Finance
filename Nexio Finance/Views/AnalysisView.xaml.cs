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
        private List<Account> _cachedAccounts = new List<Account>();
        private string[] _colors = { "#3B82F6", "#EF4444", "#10B981", "#F59E0B", "#F59E0B", "#8B5CF6", "#EC4899", "#14B8A6", "#6366F1", "#F43F5E", "#0EA5E9", "#84CC16" };

        private int? _drillDownCategoryId = null;

        public AnalysisView()
        {
            InitializeComponent();
            LoadInitialData();
            InitBalanceFilters();
        }

        private void LoadInitialData()
        {
            using (var context = new AppDbContext())
            {
                _allTransactions = context.Transactions
                    .Include(t => t.Account)
                    .Include(t => t.Category)
                    .ThenInclude(c => c.ParentCategory)
                    .ToList();

                _currencies = context.Currencies.ToList();
                _cachedAccounts = context.Accounts.ToList();

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

        private void InitBalanceFilters()
        {
            BalanceAccountFilter.SelectionChanged -= BalanceFilter_SelectionChanged;
            BalanceRangeFilter.SelectionChanged -= BalanceFilter_SelectionChanged;
            BalanceGranularityFilter.SelectionChanged -= BalanceFilter_SelectionChanged;

            BalanceAccountFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterAllAccounts ?? "Všechny účty");
            foreach (var acc in _cachedAccounts) BalanceAccountFilter.Items.Add(acc.Name);
            BalanceAccountFilter.SelectedIndex = 0;

            BalanceRangeFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterLast30Days ?? "Posledních 30 dní");
            BalanceRangeFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterThisYear ?? "Tento rok");
            BalanceRangeFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterAllTime ?? "Celé období");
            BalanceRangeFilter.SelectedIndex = 0;

            BalanceGranularityFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterByDays ?? "Po dnech");
            BalanceGranularityFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterByWeeks ?? "Po týdnech");
            BalanceGranularityFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterByMonths ?? "Po měsících");
            BalanceGranularityFilter.SelectedIndex = 0;

            BalanceAccountFilter.SelectionChanged += BalanceFilter_SelectionChanged;
            BalanceRangeFilter.SelectionChanged += BalanceFilter_SelectionChanged;
            BalanceGranularityFilter.SelectionChanged += BalanceFilter_SelectionChanged;
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateBalanceChart();
        }

        private void BalanceTrendCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateBalanceChart(); // Spolehlivé vykreslení ihned po načtení plátna
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _drillDownCategoryId = null;
            BackButton.Visibility = Visibility.Collapsed;
            BarChartTitle.Text = Nexio_Finance.Properties.Resources.AnalysisExpenseDetail ?? "Detail výdajů dle hlavních kategorií";
            UpdateDashboard();
            UpdateBalanceChart();
        }

        private void BalanceFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBalanceChart();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _drillDownCategoryId = null;
            BackButton.Visibility = Visibility.Collapsed;
            BarChartTitle.Text = Nexio_Finance.Properties.Resources.AnalysisExpenseDetail ?? "Detail výdajů dle hlavních kategorií";
            UpdateDashboard();
        }

        private void UpdateDashboard()
        {
            if (YearComboBox.SelectedItem == null || MonthComboBox.SelectedItem == null || CurrencyComboBox.SelectedItem == null) return;

            int selectedYear = (int)YearComboBox.SelectedItem;
            int selectedMonth = (int)MonthComboBox.SelectedItem;

            var targetCurrency = CurrencyComboBox.SelectedItem as Currency;
            decimal targetRate = targetCurrency.ExchangeRate != 0 ? targetCurrency.ExchangeRate : 1m;

            var filtered = _allTransactions
                .Where(t => t.Type != TransactionType.Transfer && t.Date.Year == selectedYear && t.Date.Month == selectedMonth)
                .ToList();

            var convertedData = new List<ChartTransactionItem>();
            foreach (var t in filtered)
            {
                var accCurrency = _currencies.FirstOrDefault(c => c.Code == t.Account.Currency);
                decimal rateToMain = (accCurrency != null && accCurrency.ExchangeRate != 0) ? accCurrency.ExchangeRate : 1m;

                decimal amountInMain = t.Amount * rateToMain;
                decimal finalAmount = amountInMain / targetRate;

                convertedData.Add(new ChartTransactionItem
                {
                    Category = t.Category,
                    Type = t.Type,
                    ConvertedAmount = finalAmount
                });
            }

            decimal totalInc = convertedData.Where(t => t.Type == TransactionType.Income).Sum(t => t.ConvertedAmount);
            decimal totalExp = Math.Abs(convertedData.Where(t => t.Type == TransactionType.Expense).Sum(t => t.ConvertedAmount));

            TotalIncomeText.Text = $"{totalInc:N2} {targetCurrency.Code}";
            TotalExpenseText.Text = $"{totalExp:N2} {targetCurrency.Code}";
            SavingsText.Text = $"{(totalInc - totalExp):N2} {targetCurrency.Code}";

            DrawIncomePieChart(convertedData);
            DrawExpensePieChart(convertedData);
            DrawBarChart(convertedData);
            DrawIncomeTrendChart(targetCurrency, targetRate);
        }

        // ==========================================
        // VÝVOJ CELKOVÉHO ZŮSTATKU V ČASE (Vylepšený design & mřížka)
        // ==========================================
        private void UpdateBalanceChart()
        {
            if (BalanceTrendCanvas == null || CurrencyComboBox == null || CurrencyComboBox.SelectedItem == null || !IsLoaded) return;
            BalanceTrendCanvas.Children.Clear();

            var targetCurrency = CurrencyComboBox.SelectedItem as Currency;
            decimal targetRate = targetCurrency.ExchangeRate != 0 ? targetCurrency.ExchangeRate : 1m;

            var accountsToProcess = new List<Account>();
            if (BalanceAccountFilter.SelectedIndex <= 0)
                accountsToProcess = _cachedAccounts;
            else
            {
                string accName = BalanceAccountFilter.SelectedItem.ToString();
                var acc = _cachedAccounts.FirstOrDefault(a => a.Name == accName);
                if (acc != null) accountsToProcess.Add(acc);
            }
            if (!accountsToProcess.Any()) return;

            DateTime endDate = DateTime.Now.Date;
            DateTime startDate = endDate.AddDays(-30);

            if (BalanceRangeFilter.SelectedIndex == 1) startDate = new DateTime(endDate.Year, 1, 1);
            else if (BalanceRangeFilter.SelectedIndex == 2) startDate = _allTransactions.Any() ? _allTransactions.Min(t => t.Date.Date) : endDate.AddDays(-30);

            decimal runningBalance = 0;
            foreach (var acc in accountsToProcess)
            {
                var accCurrency = _currencies.FirstOrDefault(c => c.Code == acc.Currency);
                decimal rateToMain = (accCurrency != null && accCurrency.ExchangeRate != 0) ? accCurrency.ExchangeRate : 1m;

                decimal sumPreTx = _allTransactions
                    .Where(t => t.AccountId == acc.Id && t.Date.Date < startDate)
                    .Sum(t => t.Amount);

                runningBalance += ((acc.InitialBalance + sumPreTx) * rateToMain) / targetRate;
            }

            var points = new List<Point>();
            var tooltips = new List<string>();
            DateTime current = startDate;

            while (current <= endDate)
            {
                DateTime next = current.AddDays(1);
                if (BalanceGranularityFilter.SelectedIndex == 1) next = current.AddDays(7);
                else if (BalanceGranularityFilter.SelectedIndex == 2) next = current.AddMonths(1);
                if (next > endDate && current < endDate) next = endDate.AddDays(1);

                decimal periodSum = 0;
                foreach (var acc in accountsToProcess)
                {
                    var accCurrency = _currencies.FirstOrDefault(c => c.Code == acc.Currency);
                    decimal rateToMain = (accCurrency != null && accCurrency.ExchangeRate != 0) ? accCurrency.ExchangeRate : 1m;

                    decimal txSum = _allTransactions
                        .Where(t => t.AccountId == acc.Id && t.Date.Date >= current && t.Date.Date < next)
                        .Sum(t => t.Amount);

                    periodSum += (txSum * rateToMain) / targetRate;
                }

                runningBalance += periodSum;
                tooltips.Add($"{current:dd.MM.yyyy}\n{runningBalance:N2} {targetCurrency.Code}");
                points.Add(new Point(0, (double)runningBalance));
                current = next;
            }

            if (points.Count < 2) return;

            double width = BalanceTrendCanvas.ActualWidth > 0 ? BalanceTrendCanvas.ActualWidth : 900;
            double height = BalanceTrendCanvas.ActualHeight > 0 ? BalanceTrendCanvas.ActualHeight : 240;

            double minB = points.Min(p => p.Y);
            double maxB = points.Max(p => p.Y);
            if (minB == maxB) { minB -= 10; maxB += 10; }

            double lowerBound = minB > 0 ? Math.Max(0, minB - (maxB - minB) * 0.15) : minB;
            double range = maxB - lowerBound;
            if (range == 0) range = 1;

            // Vykreslení jemných horizontálních mřížkových čar
            for (int i = 0; i <= 3; i++)
            {
                double gridY = (height / 3) * i;
                decimal gridVal = (decimal)(maxB - (range / 3) * i);

                Line gridLine = new Line
                {
                    X1 = 0,
                    Y1 = gridY,
                    X2 = width,
                    Y2 = gridY,
                    Stroke = (Brush)new BrushConverter().ConvertFromString("#E5E7EB"),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection(new double[] { 4, 4 })
                };
                BalanceTrendCanvas.Children.Add(gridLine);

                TextBlock lbl = new TextBlock
                {
                    Text = $"{gridVal:N0} {targetCurrency.Code}",
                    FontSize = 10,
                    Foreground = Brushes.Gray
                };
                Canvas.SetLeft(lbl, 0);
                Canvas.SetTop(lbl, gridY - 14);
                BalanceTrendCanvas.Children.Add(lbl);
            }

            var drawPoints = new PointCollection();
            for (int i = 0; i < points.Count; i++)
            {
                double x = (width / (points.Count - 1)) * i;
                double y = height - ((points[i].Y - lowerBound) / range * height);
                drawPoints.Add(new Point(x, y));

                Ellipse marker = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = Brushes.White,
                    Stroke = (Brush)new BrushConverter().ConvertFromString("#3B82F6"),
                    StrokeThickness = 2,
                    ToolTip = tooltips[i]
                };
                Canvas.SetLeft(marker, x - 3.5);
                Canvas.SetTop(marker, y - 3.5);
                BalanceTrendCanvas.Children.Add(marker);
            }

            // Výplň pod čarou (Area gradient)
            var areaPoints = new PointCollection(drawPoints);
            areaPoints.Add(new Point(width, height));
            areaPoints.Add(new Point(0, height));

            Polygon area = new Polygon
            {
                Points = areaPoints,
                Fill = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#303B82F6"),
                    (Color)ColorConverter.ConvertFromString("#053B82F6"),
                    new Point(0, 0), new Point(0, 1))
            };
            BalanceTrendCanvas.Children.Add(area);

            // Hlavní křivka zůstatku
            Polyline line = new Polyline
            {
                Points = drawPoints,
                Stroke = (Brush)new BrushConverter().ConvertFromString("#3B82F6"),
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round
            };
            BalanceTrendCanvas.Children.Add(line);

            // Popisky osy X
            TextBlock txtStart = new TextBlock { Text = startDate.ToString("dd. MM. yyyy"), Foreground = Brushes.Gray, FontSize = 11 };
            Canvas.SetLeft(txtStart, 0);
            Canvas.SetBottom(txtStart, -25);
            BalanceTrendCanvas.Children.Add(txtStart);

            TextBlock txtEnd = new TextBlock { Text = endDate.ToString("dd. MM. yyyy"), Foreground = Brushes.Gray, FontSize = 11 };
            Canvas.SetRight(txtEnd, 0);
            Canvas.SetBottom(txtEnd, -25);
            BalanceTrendCanvas.Children.Add(txtEnd);
        }

        // ==========================================
        // VÝVOJ PŘÍJMŮ
        // ==========================================
        private void DrawIncomeTrendChart(Currency targetCurrency, decimal targetRate)
        {
            IncomeTrendCanvas.Children.Clear();
            var allIncomes = _allTransactions.Where(t => t.Type == TransactionType.Income).ToList();
            if (!allIncomes.Any()) return;

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
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();

            if (!grouped.Any()) return;

            double canvasHeight = 140;
            double maxAmount = (double)grouped.Max(g => g.Amount);
            if (maxAmount == 0) maxAmount = 1;

            double barWidth = 45;
            double spacing = 30;
            double currentX = 20;

            IncomeTrendCanvas.Width = grouped.Count * (barWidth + spacing) + 40;

            for (int i = 0; i < grouped.Count; i++)
            {
                var item = grouped[i];
                double barHeight = ((double)item.Amount / maxAmount) * canvasHeight;
                if (barHeight < 5) barHeight = 5;

                Rectangle bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(bar, currentX);
                Canvas.SetBottom(bar, 25);
                IncomeTrendCanvas.Children.Add(bar);

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
        // KOLÁČOVÉ GRAFY
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
        // SLOUPCOVÝ GRAF VÝDAJŮ
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

            double canvasHeight = 160;
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

    public class ChartTransactionItem
    {
        public Category Category { get; set; }
        public TransactionType Type { get; set; }
        public decimal ConvertedAmount { get; set; }
    }

    public class CategoryChartItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Amount { get; set; }
    }
}