using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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

        // Moderní ucelená Fintech paleta
        private readonly string[] _colors =
        {
            "#3B82F6", // Blue
            "#EF4444", // Red
            "#10B981", // Emerald
            "#F59E0B", // Amber
            "#8B5CF6", // Violet
            "#EC4899", // Pink
            "#06B6D4", // Cyan
            "#6366F1", // Indigo
            "#F97316", // Orange
            "#14B8A6", // Teal
            "#84CC16", // Lime
            "#F43F5E"  // Rose
        };

        private int? _drillDownCategoryId = null;
        private List<ChartTransactionItem> _lastConvertedData = new List<ChartTransactionItem>();

        // Stav pro interaktivní Crosshair v grafu zůstatku
        private List<BalanceDataPoint> _balancePoints = new List<BalanceDataPoint>();
        private Line? _hoverVerticalLine;
        private Ellipse? _hoverOuterMarker;
        private Ellipse? _hoverInnerMarker;
        private Border? _hoverTooltipCard;
        private TextBlock? _hoverDateText;
        private TextBlock? _hoverAmountText;
        private TextBlock? _hoverDeltaText;

        public AnalysisView()
        {
            InitializeComponent();
            LoadInitialData();
            InitBalanceFilters();
            UpdateDashboard();
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
                if (!years.Contains(DateTime.Now.Year)) years.Insert(0, DateTime.Now.Year);
                years = years.Distinct().OrderByDescending(y => y).ToList();

                YearComboBox.SelectionChanged -= Filter_SelectionChanged;
                YearComboBox.ItemsSource = years;
                if (years.Count > 0) YearComboBox.SelectedIndex = 0;
                YearComboBox.SelectionChanged += Filter_SelectionChanged;

                var months = Enumerable.Range(1, 12).ToList();
                MonthComboBox.SelectionChanged -= Filter_SelectionChanged;
                MonthComboBox.ItemsSource = months;

                // Předvybereme nejnovější měsíc s daty v daném roce, případně aktuální měsíc
                int selectedYear = (int)(YearComboBox.SelectedItem ?? DateTime.Now.Year);
                var latestTxInYear = _allTransactions.Where(t => t.Date.Year == selectedYear).OrderByDescending(t => t.Date).FirstOrDefault();
                MonthComboBox.SelectedItem = latestTxInYear != null ? latestTxInYear.Date.Month : DateTime.Now.Month;
                MonthComboBox.SelectionChanged += Filter_SelectionChanged;
            }
        }

        private void InitBalanceFilters()
        {
            BalanceAccountFilter.SelectionChanged -= BalanceFilter_SelectionChanged;
            BalanceRangeFilter.SelectionChanged -= BalanceFilter_SelectionChanged;
            BalanceGranularityFilter.SelectionChanged -= BalanceFilter_SelectionChanged;

            BalanceAccountFilter.Items.Clear();
            BalanceAccountFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterAllAccounts ?? "Všechny účty");
            foreach (var acc in _cachedAccounts) BalanceAccountFilter.Items.Add(acc.Name);
            BalanceAccountFilter.SelectedIndex = 0;

            BalanceRangeFilter.Items.Clear();
            BalanceRangeFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterLast30Days ?? "Posledních 30 dní");
            BalanceRangeFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterThisYear ?? "Tento rok");
            BalanceRangeFilter.Items.Add(Nexio_Finance.Properties.Resources.FilterAllTime ?? "Celé období");
            BalanceRangeFilter.SelectedIndex = 0;

            BalanceGranularityFilter.Items.Clear();
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
            if (!IsLoaded) return;
            DrawBarChart(_lastConvertedData);
            if (CurrencyComboBox.SelectedItem is Currency targetCurrency)
            {
                decimal targetRate = targetCurrency.ExchangeRate != 0 ? targetCurrency.ExchangeRate : 1m;
                DrawIncomeTrendChart(targetCurrency, targetRate);
            }
            UpdateBalanceChart();
        }

        private void BalanceTrendCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            DrawBarChart(_lastConvertedData);
            if (CurrencyComboBox.SelectedItem is Currency targetCurrency)
            {
                decimal targetRate = targetCurrency.ExchangeRate != 0 ? targetCurrency.ExchangeRate : 1m;
                DrawIncomeTrendChart(targetCurrency, targetRate);
            }
            UpdateBalanceChart();
        }

        private void PrevMonthButton_Click(object sender, RoutedEventArgs e)
        {
            ShiftSelectedPeriod(-1);
        }

        private void NextMonthButton_Click(object sender, RoutedEventArgs e)
        {
            ShiftSelectedPeriod(1);
        }

        private void ShiftSelectedPeriod(int monthDelta)
        {
            if (YearComboBox.SelectedItem == null || MonthComboBox.SelectedItem == null) return;
            int year = (int)YearComboBox.SelectedItem;
            int month = (int)MonthComboBox.SelectedItem;

            DateTime dt = new DateTime(year, month, 1).AddMonths(monthDelta);
            var years = YearComboBox.ItemsSource as List<int>;
            if (years != null && !years.Contains(dt.Year))
            {
                years.Add(dt.Year);
                years.Sort((a, b) => b.CompareTo(a));
                YearComboBox.ItemsSource = null;
                YearComboBox.ItemsSource = years;
            }

            YearComboBox.SelectionChanged -= Filter_SelectionChanged;
            MonthComboBox.SelectionChanged -= Filter_SelectionChanged;

            YearComboBox.SelectedItem = dt.Year;
            MonthComboBox.SelectedItem = dt.Month;

            YearComboBox.SelectionChanged += Filter_SelectionChanged;
            MonthComboBox.SelectionChanged += Filter_SelectionChanged;

            Filter_SelectionChanged(this, null!);
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _drillDownCategoryId = null;
            BackButton.Visibility = Visibility.Collapsed;
            DrillDownHintBadge.Visibility = Visibility.Visible;
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
            DrillDownHintBadge.Visibility = Visibility.Visible;
            BarChartTitle.Text = Nexio_Finance.Properties.Resources.AnalysisExpenseDetail ?? "Detail výdajů dle hlavních kategorií";
            UpdateDashboard();
        }

        private decimal ConvertAmount(Transaction t, decimal targetRate)
        {
            var accCurrency = _currencies.FirstOrDefault(c => c.Code == t.Account?.Currency);
            decimal rateToMain = (accCurrency != null && accCurrency.ExchangeRate != 0) ? accCurrency.ExchangeRate : 1m;
            return (t.Amount * rateToMain) / targetRate;
        }

        private void UpdateDashboard()
        {
            if (YearComboBox.SelectedItem == null || MonthComboBox.SelectedItem == null || CurrencyComboBox.SelectedItem == null) return;

            int selectedYear = (int)YearComboBox.SelectedItem;
            int selectedMonth = (int)MonthComboBox.SelectedItem;

            var targetCurrency = CurrencyComboBox.SelectedItem as Currency;
            if (targetCurrency == null) return;
            decimal targetRate = targetCurrency.ExchangeRate != 0 ? targetCurrency.ExchangeRate : 1m;

            // Aktualizace titulku období
            DateTime periodDate = new DateTime(selectedYear, selectedMonth, 1);
            string monthName = CultureInfo.CurrentUICulture.DateTimeFormat.GetMonthName(selectedMonth);
            if (!string.IsNullOrEmpty(monthName))
                monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);
            ActivePeriodTitle.Text = $"Finanční analýza — {monthName} {selectedYear}";

            var filtered = _allTransactions
                .Where(t => t.Type != TransactionType.Transfer && t.Date.Year == selectedYear && t.Date.Month == selectedMonth)
                .ToList();

            var convertedData = new List<ChartTransactionItem>();
            foreach (var t in filtered)
            {
                convertedData.Add(new ChartTransactionItem
                {
                    Category = t.Category,
                    Type = t.Type,
                    ConvertedAmount = ConvertAmount(t, targetRate)
                });
            }
            _lastConvertedData = convertedData;

            decimal totalInc = convertedData.Where(t => t.Type == TransactionType.Income).Sum(t => t.ConvertedAmount);
            decimal totalExp = Math.Abs(convertedData.Where(t => t.Type == TransactionType.Expense).Sum(t => t.ConvertedAmount));
            decimal netSavings = totalInc - totalExp;

            // Výpočet předchozího měsíce pro MoM % srovnání
            DateTime prevDate = periodDate.AddMonths(-1);
            var prevFiltered = _allTransactions
                .Where(t => t.Type != TransactionType.Transfer && t.Date.Year == prevDate.Year && t.Date.Month == prevDate.Month)
                .ToList();

            decimal prevInc = prevFiltered.Where(t => t.Type == TransactionType.Income).Sum(t => ConvertAmount(t, targetRate));
            decimal prevExp = Math.Abs(prevFiltered.Where(t => t.Type == TransactionType.Expense).Sum(t => ConvertAmount(t, targetRate)));

            // Texty hlavních KPI karet
            TotalIncomeText.Text = $"{totalInc:N2} {targetCurrency.Code}";
            TotalExpenseText.Text = $"{totalExp:N2} {targetCurrency.Code}";
            SavingsText.Text = $"{(netSavings >= 0 ? "+" : "")}{netSavings:N2} {targetCurrency.Code}";
            SavingsText.Foreground = BrushFromHex(netSavings >= 0 ? "#2563EB" : "#DC2626");

            int incCount = convertedData.Count(t => t.Type == TransactionType.Income);
            int expCount = convertedData.Count(t => t.Type == TransactionType.Expense);
            int daysInMonth = DateTime.DaysInMonth(selectedYear, selectedMonth);
            decimal avgDailyExpense = daysInMonth > 0 ? totalExp / daysInMonth : 0;

            IncomeSubText.Text = $"{incCount} příjmových transakcí v období";
            ExpenseSubText.Text = $"{expCount} výdajů • Ø {avgDailyExpense:N0} {targetCurrency.Code} / den";

            // MoM Badge pro Příjmy
            UpdateMomBadge(IncomeTrendBadge, IncomeTrendBadgeText, totalInc, prevInc, invertColors: false);
            // MoM Badge pro Náklady (nižší náklady = zelená)
            UpdateMomBadge(ExpenseTrendBadge, ExpenseTrendBadgeText, totalExp, prevExp, invertColors: true);

            // Míra úspor a progress bar
            if (totalInc > 0)
            {
                decimal savingsRate = (netSavings / totalInc) * 100m;
                decimal spentRatio = Math.Clamp((totalExp / totalInc), 0m, 1m);

                SavingsRateBadgeText.Text = $"Úspora: {savingsRate:N1} %";
                SavingsRateBadge.Background = BrushFromHex(savingsRate >= 0 ? "#EFF6FF" : "#FEF2F2");
                SavingsRateBadgeText.Foreground = BrushFromHex(savingsRate >= 0 ? "#2563EB" : "#DC2626");

                SavingsProgressBar.Width = (double)spentRatio * 180.0;
                SavingsProgressBar.Background = BrushFromHex(totalExp <= totalInc ? "#3B82F6" : "#EF4444");
                SavingsRatioSubText.Text = $"{(totalExp / totalInc) * 100m:N0} % příjmů utraceno";
            }
            else
            {
                SavingsRateBadgeText.Text = totalExp > 0 ? "Bez příjmů" : "0 %";
                SavingsRateBadge.Background = BrushFromHex("#F1F5F9");
                SavingsRateBadgeText.Foreground = BrushFromHex("#64748B");
                SavingsProgressBar.Width = totalExp > 0 ? 180 : 0;
                SavingsProgressBar.Background = BrushFromHex("#EF4444");
                SavingsRatioSubText.Text = totalExp > 0 ? "100 % čerpání z rezerv" : "Žádná aktivita";
            }

            DrawIncomePieChart(convertedData, targetCurrency.Code);
            DrawExpensePieChart(convertedData, targetCurrency.Code);
            DrawBarChart(convertedData);
            DrawIncomeTrendChart(targetCurrency, targetRate);
        }

        private void UpdateMomBadge(Border badge, TextBlock textBlock, decimal current, decimal previous, bool invertColors)
        {
            if (previous <= 0)
            {
                badge.Background = BrushFromHex("#F1F5F9");
                textBlock.Foreground = BrushFromHex("#64748B");
                textBlock.Text = "Nové období";
                return;
            }

            decimal diffPct = ((current - previous) / previous) * 100m;
            string sign = diffPct >= 0 ? "▲ +" : "▼ ";
            textBlock.Text = $"{sign}{diffPct:N1} % vs min. m.";

            bool isPositiveOutcome = invertColors ? diffPct <= 0 : diffPct >= 0;
            badge.Background = BrushFromHex(isPositiveOutcome ? "#ECFDF5" : "#FEF2F2");
            textBlock.Foreground = BrushFromHex(isPositiveOutcome ? "#059669" : "#DC2626");
        }

        // ==========================================
        // PRSTENCOVÉ (DONUT) GRAFY S BOHATOU LEGENDOU
        // ==========================================
        private void DrawIncomePieChart(List<ChartTransactionItem> data, string currencyCode)
        {
            var grouped = data.Where(t => t.Type == TransactionType.Income && t.Category != null)
                .GroupBy(t => t.Category.ParentCategory ?? t.Category)
                .Select(g => new CategoryChartItem
                {
                    Id = g.Key.Id,
                    Name = g.Key.Name,
                    Amount = g.Sum(t => t.ConvertedAmount),
                    Count = g.Count()
                })
                .Where(x => x.Amount > 0)
                .OrderByDescending(x => x.Amount)
                .ToList();

            decimal total = grouped.Sum(x => x.Amount);
            IncomeDonutCenterLabel.Text = "PŘÍJMY";
            IncomeDonutCenterValue.Text = $"{total:N0} {currencyCode}";
            IncomeDonutCenterSub.Text = $"{grouped.Count} {(grouped.Count == 1 ? "kategorie" : "kategorií")}";

            DrawDonut(IncomePieCanvas, IncomeLegend, grouped, currencyCode, isExpenseChart: false);
        }

        private void DrawExpensePieChart(List<ChartTransactionItem> data, string currencyCode)
        {
            var grouped = data.Where(t => t.Type == TransactionType.Expense && t.Category != null)
                .GroupBy(t => t.Category.ParentCategory ?? t.Category)
                .Select(g => new CategoryChartItem
                {
                    Id = g.Key.Id,
                    Name = g.Key.Name,
                    Amount = Math.Abs(g.Sum(t => t.ConvertedAmount)),
                    Count = g.Count()
                })
                .Where(x => x.Amount > 0)
                .OrderByDescending(x => x.Amount)
                .ToList();

            decimal total = grouped.Sum(x => x.Amount);
            ExpenseDonutCenterLabel.Text = "NÁKLADY";
            ExpenseDonutCenterValue.Text = $"{total:N0} {currencyCode}";
            ExpenseDonutCenterSub.Text = $"{grouped.Count} {(grouped.Count == 1 ? "kategorie" : "kategorií")}";

            DrawDonut(ExpensePieCanvas, ExpenseLegend, grouped, currencyCode, isExpenseChart: true);
        }

        private void DrawDonut(Canvas canvas, StackPanel legend, List<CategoryChartItem> items, string currencyCode, bool isExpenseChart)
        {
            canvas.Children.Clear();
            legend.Children.Clear();

            double size = 184;
            Point center = new Point(size / 2, size / 2);
            double outerRadius = 84;
            double innerRadius = 56;
            double ringRadius = (outerRadius + innerRadius) / 2.0;
            double ringThickness = outerRadius - innerRadius;

            // Podkladový prstenec
            Ellipse bgRing = new Ellipse
            {
                Width = ringRadius * 2,
                Height = ringRadius * 2,
                Stroke = BrushFromHex("#F1F5F9"),
                StrokeThickness = ringThickness
            };
            Canvas.SetLeft(bgRing, center.X - ringRadius);
            Canvas.SetTop(bgRing, center.Y - ringRadius);
            canvas.Children.Add(bgRing);

            double total = (double)items.Sum(x => x.Amount);
            if (total <= 0)
            {
                TextBlock emptyTxt = new TextBlock
                {
                    Text = "Žádná data v vybraném měsíci",
                    Foreground = BrushFromHex("#94A3B8"),
                    FontSize = 13,
                    Margin = new Thickness(8, 20, 0, 0)
                };
                legend.Children.Add(emptyTxt);
                return;
            }

            double currentAngle = -Math.PI / 2;
            double gapRadians = items.Count > 1 ? 0.035 : 0; // Jemná mezera mezi segmenty

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                double share = (double)item.Amount / total;
                string hexColor = _colors[i % _colors.Length];
                Brush segmentBrush = BrushFromHex(hexColor);

                double sweepAngle = Math.Max(0.02, share * 2 * Math.PI - gapRadians);
                double startAngle = currentAngle + (gapRadians / 2);
                double endAngle = startAngle + sweepAngle;
                currentAngle += share * 2 * Math.PI;

                UIElement sliceElement;

                if (items.Count == 1 || share >= 0.995)
                {
                    Ellipse fullRing = new Ellipse
                    {
                        Width = ringRadius * 2,
                        Height = ringRadius * 2,
                        Stroke = segmentBrush,
                        StrokeThickness = ringThickness,
                        Cursor = isExpenseChart ? Cursors.Hand : Cursors.Arrow,
                        ToolTip = $"{item.Name}: {item.Amount:N2} {currencyCode} ({share:P1})"
                    };
                    Canvas.SetLeft(fullRing, center.X - ringRadius);
                    Canvas.SetTop(fullRing, center.Y - ringRadius);
                    sliceElement = fullRing;
                }
                else
                {
                    Path slicePath = CreateDonutSlice(center, outerRadius, innerRadius, startAngle, endAngle, segmentBrush);
                    slicePath.Cursor = isExpenseChart ? Cursors.Hand : Cursors.Arrow;
                    slicePath.ToolTip = $"{item.Name}: {item.Amount:N2} {currencyCode} ({share:P1})";

                    // Interaktivní Hover efekt na výseči
                    slicePath.MouseEnter += (s, e) =>
                    {
                        slicePath.Opacity = 0.85;
                        if (isExpenseChart)
                        {
                            ExpenseDonutCenterLabel.Text = item.Name.ToUpperInvariant();
                            ExpenseDonutCenterValue.Text = $"{item.Amount:N0} {currencyCode}";
                            ExpenseDonutCenterSub.Text = $"{share:P1} z výdajů";
                        }
                        else
                        {
                            IncomeDonutCenterLabel.Text = item.Name.ToUpperInvariant();
                            IncomeDonutCenterValue.Text = $"{item.Amount:N0} {currencyCode}";
                            IncomeDonutCenterSub.Text = $"{share:P1} z příjmů";
                        }
                    };
                    slicePath.MouseLeave += (s, e) =>
                    {
                        slicePath.Opacity = 1.0;
                        if (isExpenseChart)
                        {
                            ExpenseDonutCenterLabel.Text = "NÁKLADY";
                            ExpenseDonutCenterValue.Text = $"{total:N0} {currencyCode}";
                            ExpenseDonutCenterSub.Text = $"{items.Count} {(items.Count == 1 ? "kategorie" : "kategorií")}";
                        }
                        else
                        {
                            IncomeDonutCenterLabel.Text = "PŘÍJMY";
                            IncomeDonutCenterValue.Text = $"{total:N0} {currencyCode}";
                            IncomeDonutCenterSub.Text = $"{items.Count} {(items.Count == 1 ? "kategorie" : "kategorií")}";
                        }
                    };

                    sliceElement = slicePath;
                }

                if (isExpenseChart)
                {
                    int catId = item.Id;
                    string catName = item.Name;
                    sliceElement.MouseLeftButtonUp += (s, e) => TriggerCategoryDrillDown(catId, catName);
                }

                canvas.Children.Add(sliceElement);

                // Bohatý řádek legendy (využívá plnou šířku pravého sloupce)
                Border rowBorder = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 0, 4, 5),
                    Background = BrushFromHex("#F8FAFC"),
                    BorderBrush = BrushFromHex("#F1F5F9"),
                    BorderThickness = new Thickness(1),
                    Cursor = isExpenseChart ? Cursors.Hand : Cursors.Arrow
                };

                Grid rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

                // Barevná tečka
                Border colorDot = new Border
                {
                    Width = 10,
                    Height = 10,
                    CornerRadius = new CornerRadius(5),
                    Background = segmentBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(colorDot, 0);
                rowGrid.Children.Add(colorDot);

                // Název kategorie
                TextBlock nameBlock = new TextBlock
                {
                    Text = item.Name,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = BrushFromHex("#1E293B"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(nameBlock, 1);
                rowGrid.Children.Add(nameBlock);

                // Částka
                TextBlock amountBlock = new TextBlock
                {
                    Text = $"{item.Amount:N0} {currencyCode}",
                    FontSize = 12.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = BrushFromHex("#0F172A"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(amountBlock, 2);
                rowGrid.Children.Add(amountBlock);

                // Procentuální Badge
                Border pctBadge = new Border
                {
                    Background = BrushFromHex("#FFFFFF"),
                    BorderBrush = BrushFromHex("#E2E8F0"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(5, 2, 5, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                pctBadge.Child = new TextBlock
                {
                    Text = $"{share * 100:N1}%",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = segmentBrush,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetColumn(pctBadge, 3);
                rowGrid.Children.Add(pctBadge);

                rowBorder.Child = rowGrid;

                rowBorder.MouseEnter += (s, e) => rowBorder.Background = BrushFromHex("#EFF6FF");
                rowBorder.MouseLeave += (s, e) => rowBorder.Background = BrushFromHex("#F8FAFC");

                if (isExpenseChart)
                {
                    int catId = item.Id;
                    string catName = item.Name;
                    rowBorder.MouseLeftButtonUp += (s, e) => TriggerCategoryDrillDown(catId, catName);
                }

                legend.Children.Add(rowBorder);
            }
        }

        private Path CreateDonutSlice(Point center, double outerRadius, double innerRadius, double startAngle, double endAngle, Brush fill)
        {
            bool isLargeArc = (endAngle - startAngle) > Math.PI;

            Point outerStart = new Point(center.X + outerRadius * Math.Cos(startAngle), center.Y + outerRadius * Math.Sin(startAngle));
            Point outerEnd = new Point(center.X + outerRadius * Math.Cos(endAngle), center.Y + outerRadius * Math.Sin(endAngle));
            Point innerEnd = new Point(center.X + innerRadius * Math.Cos(endAngle), center.Y + innerRadius * Math.Sin(endAngle));
            Point innerStart = new Point(center.X + innerRadius * Math.Cos(startAngle), center.Y + innerRadius * Math.Sin(startAngle));

            PathFigure figure = new PathFigure { StartPoint = outerStart, IsClosed = true };
            figure.Segments.Add(new ArcSegment(outerEnd, new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(innerEnd, true));
            figure.Segments.Add(new ArcSegment(innerStart, new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.Counterclockwise, true));

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return new Path
            {
                Fill = fill,
                Data = geometry
            };
        }

        private void TriggerCategoryDrillDown(int categoryId, string categoryName)
        {
            _drillDownCategoryId = categoryId;
            BarChartTitle.Text = $"Detail podkategorií: {categoryName}";
            BackButton.Visibility = Visibility.Visible;
            DrillDownHintBadge.Visibility = Visibility.Collapsed;
            DrawBarChart(_lastConvertedData);
        }

        // ==========================================
        // RESPONZIVNÍ DVOUSLOUPCOVÝ BREAKDOWN VÝDAJŮ PŘES CELOU ŠÍŘKU
        // ==========================================
        private void DrawBarChart(List<ChartTransactionItem> data)
        {
            if (ExpenseBarCanvas == null) return;
            ExpenseBarCanvas.Children.Clear();

            var targetCurrency = CurrencyComboBox?.SelectedItem as Currency;
            string currencyCode = targetCurrency?.Code ?? "CZK";

            var relevantData = data.Where(t => t.Type == TransactionType.Expense && t.Category != null).ToList();
            var grouped = new List<CategoryChartItem>();

            if (_drillDownCategoryId == null)
            {
                grouped = relevantData
                    .GroupBy(t => t.Category.ParentCategory ?? t.Category)
                    .Select(g => new CategoryChartItem
                    {
                        Id = g.Key.Id,
                        Name = g.Key.Name,
                        Amount = Math.Abs(g.Sum(t => t.ConvertedAmount)),
                        Count = g.Count()
                    })
                    .Where(x => x.Amount > 0)
                    .OrderByDescending(x => x.Amount)
                    .ToList();
            }
            else
            {
                grouped = relevantData
                    .Where(t => t.Category.ParentCategoryId == _drillDownCategoryId || t.Category.Id == _drillDownCategoryId)
                    .GroupBy(t => t.Category)
                    .Select(g => new CategoryChartItem
                    {
                        Id = g.Key.Id,
                        Name = g.Key.Name,
                        Amount = Math.Abs(g.Sum(t => t.ConvertedAmount)),
                        Count = g.Count()
                    })
                    .Where(x => x.Amount > 0)
                    .OrderByDescending(x => x.Amount)
                    .ToList();
            }

            double canvasWidth = ExpenseBarCanvas.ActualWidth > 100 ? ExpenseBarCanvas.ActualWidth : 1080;

            if (grouped.Count == 0)
            {
                ExpenseBarCanvas.Height = 90;
                TextBlock emptyBlock = new TextBlock
                {
                    Text = "Žádné výdaje k zobrazení pro vybrané období.",
                    FontSize = 13,
                    Foreground = BrushFromHex("#94A3B8")
                };
                Canvas.SetLeft(emptyBlock, 10);
                Canvas.SetTop(emptyBlock, 30);
                ExpenseBarCanvas.Children.Add(emptyBlock);
                return;
            }

            double maxAmount = (double)grouped.Max(g => g.Amount);
            double totalExpense = (double)grouped.Sum(g => g.Amount);
            if (maxAmount <= 0) maxAmount = 1;
            if (totalExpense <= 0) totalExpense = 1;

            // Dvousloupcový grid na širokých obrazovkách, jednosloupcový na užších
            int columns = (canvasWidth >= 760 && grouped.Count > 1) ? 2 : 1;
            double colGap = 24;
            double colWidth = columns == 2 ? (canvasWidth - colGap) / 2.0 : canvasWidth;
            double rowHeight = 62;
            double rowGap = 12;

            int rows = (int)Math.Ceiling(grouped.Count / (double)columns);
            ExpenseBarCanvas.Height = Math.Max(100, rows * (rowHeight + rowGap));

            for (int i = 0; i < grouped.Count; i++)
            {
                var item = grouped[i];
                int col = i % columns;
                int row = i / columns;

                double x = col * (colWidth + colGap);
                double y = row * (rowHeight + rowGap);

                string hexColor = _colors[i % _colors.Length];
                Brush accentBrush = BrushFromHex(hexColor);
                double shareOfTotal = (double)item.Amount / totalExpense;
                double barRatio = (double)item.Amount / maxAmount;

                Border itemCard = new Border
                {
                    Width = colWidth,
                    Height = rowHeight,
                    Background = BrushFromHex("#F8FAFC"),
                    BorderBrush = BrushFromHex("#E2E8F0"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 10, 14, 10),
                    Cursor = _drillDownCategoryId == null ? Cursors.Hand : Cursors.Arrow,
                    Tag = item.Id
                };

                Grid innerGrid = new Grid();
                innerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                innerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Horní řádek: Pořadí + Název kategorie + Počet transakcí vlevo, Částka + % vpravo
                Grid topRow = new Grid();
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                StackPanel leftInfo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Border rankBadge = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(6),
                    Background = accentBrush,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                rankBadge.Child = new TextBlock
                {
                    Text = $"{i + 1}",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                leftInfo.Children.Add(rankBadge);

                TextBlock catTitle = new TextBlock
                {
                    Text = item.Name,
                    FontSize = 13.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = BrushFromHex("#0F172A"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                leftInfo.Children.Add(catTitle);

                TextBlock txCountText = new TextBlock
                {
                    Text = $"({item.Count}×)",
                    FontSize = 11.5,
                    Foreground = BrushFromHex("#64748B"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                leftInfo.Children.Add(txCountText);

                Grid.SetColumn(leftInfo, 0);
                topRow.Children.Add(leftInfo);

                StackPanel rightInfo = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                TextBlock amountLbl = new TextBlock
                {
                    Text = $"{item.Amount:N2} {currencyCode}",
                    FontSize = 13.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = BrushFromHex("#0F172A"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                rightInfo.Children.Add(amountLbl);

                TextBlock pctLbl = new TextBlock
                {
                    Text = $"{shareOfTotal * 100:N1} %",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = accentBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 44,
                    TextAlignment = TextAlignment.Right
                };
                rightInfo.Children.Add(pctLbl);

                Grid.SetColumn(rightInfo, 1);
                topRow.Children.Add(rightInfo);

                Grid.SetRow(topRow, 0);
                innerGrid.Children.Add(topRow);

                // Spodní řádek: Progress bar přes celou šířku karty
                double trackWidth = Math.Max(60, colWidth - 30);
                Border trackBar = new Border
                {
                    Height = 8,
                    Width = trackWidth,
                    Background = BrushFromHex("#E2E8F0"),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 8, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                Border fillBar = new Border
                {
                    Height = 8,
                    Width = Math.Max(6, trackWidth * barRatio),
                    Background = accentBrush,
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                trackBar.Child = fillBar;

                Grid.SetRow(trackBar, 1);
                innerGrid.Children.Add(trackBar);

                itemCard.Child = innerGrid;

                itemCard.MouseEnter += (s, e) =>
                {
                    itemCard.Background = BrushFromHex("#EFF6FF");
                    itemCard.BorderBrush = BrushFromHex("#BFDBFE");
                };
                itemCard.MouseLeave += (s, e) =>
                {
                    itemCard.Background = BrushFromHex("#F8FAFC");
                    itemCard.BorderBrush = BrushFromHex("#E2E8F0");
                };

                if (_drillDownCategoryId == null)
                {
                    int catId = item.Id;
                    string catName = item.Name;
                    itemCard.MouseLeftButtonUp += (s, e) => TriggerCategoryDrillDown(catId, catName);
                }

                Canvas.SetLeft(itemCard, x);
                Canvas.SetTop(itemCard, y);
                ExpenseBarCanvas.Children.Add(itemCard);
            }
        }

        // ==========================================
        // SROVNÁVACÍ GRAF CASHFLOW (PŘÍJMY VS. NÁKLADY + ČISTÁ BILANCE)
        // ==========================================
        private void DrawIncomeTrendChart(Currency targetCurrency, decimal targetRate)
        {
            if (IncomeTrendCanvas == null) return;
            IncomeTrendCanvas.Children.Clear();

            var nonTransfers = _allTransactions.Where(t => t.Type != TransactionType.Transfer).ToList();
            if (!nonTransfers.Any()) return;

            var grouped = nonTransfers
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g =>
                {
                    decimal inc = g.Where(t => t.Type == TransactionType.Income).Sum(t => ConvertAmount(t, targetRate));
                    decimal exp = Math.Abs(g.Where(t => t.Type == TransactionType.Expense).Sum(t => ConvertAmount(t, targetRate)));
                    return new
                    {
                        Year = g.Key.Year,
                        Month = g.Key.Month,
                        Income = inc,
                        Expense = exp,
                        Net = inc - exp
                    };
                })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();

            if (!grouped.Any()) return;

            int selectedYear = YearComboBox.SelectedItem is int y ? y : -1;
            int selectedMonth = MonthComboBox.SelectedItem is int m ? m : -1;

            double chartTop = 24;
            double chartBottom = 34;
            double chartHeight = 165;
            double totalHeight = chartTop + chartHeight + chartBottom;
            IncomeTrendCanvas.Height = totalHeight;

            double maxVal = (double)grouped.Max(g => Math.Max(g.Income, g.Expense));
            if (maxVal <= 0) maxVal = 1;

            // Dynamická šířka tak, aby graf vyplnil celý ScrollViewer a při hodně měsících plynule roloval
            double viewerWidth = CashflowScrollViewer?.ActualWidth > 100 ? CashflowScrollViewer.ActualWidth : 1080;
            double minGroupSlot = 68;
            double slotWidth = Math.Max(minGroupSlot, (viewerWidth - 40) / Math.Max(1, grouped.Count));
            double canvasWidth = Math.Max(viewerWidth, grouped.Count * slotWidth + 40);
            IncomeTrendCanvas.Width = canvasWidth;

            // Jemné horizontální mřížkové linky
            for (int l = 0; l <= 3; l++)
            {
                double lineY = chartTop + (chartHeight / 3.0) * l;
                Line gridLine = new Line
                {
                    X1 = 0,
                    Y1 = lineY,
                    X2 = canvasWidth,
                    Y2 = lineY,
                    Stroke = BrushFromHex("#F1F5F9"),
                    StrokeThickness = 1
                };
                IncomeTrendCanvas.Children.Add(gridLine);
            }

            var netPoints = new List<Point>();

            LinearGradientBrush incBrush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#10B981"),
                (Color)ColorConverter.ConvertFromString("#059669"),
                new Point(0, 0), new Point(0, 1));

            LinearGradientBrush expBrush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#F87171"),
                (Color)ColorConverter.ConvertFromString("#EF4444"),
                new Point(0, 0), new Point(0, 1));

            double singleBarWidth = Math.Clamp(slotWidth * 0.28, 14, 24);
            double barGap = 4;

            for (int i = 0; i < grouped.Count; i++)
            {
                var item = grouped[i];
                double slotLeft = 20 + i * slotWidth;
                double centerX = slotLeft + slotWidth / 2.0;

                bool isSelectedMonth = (item.Year == selectedYear && item.Month == selectedMonth);

                // Podsvícení vybraného měsíce
                Border slotHighlight = new Border
                {
                    Width = slotWidth - 6,
                    Height = totalHeight - 8,
                    CornerRadius = new CornerRadius(8),
                    Background = isSelectedMonth ? BrushFromHex("#EFF6FF") : Brushes.Transparent,
                    BorderBrush = isSelectedMonth ? BrushFromHex("#BFDBFE") : Brushes.Transparent,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{item.Month:00}/{item.Year}\nPříjmy: {item.Income:N0} {targetCurrency.Code}\nNáklady: {item.Expense:N0} {targetCurrency.Code}\nBilance: {(item.Net >= 0 ? "+" : "")}{item.Net:N0} {targetCurrency.Code}\n(Kliknutím přepnete filtr období)"
                };

                int targetY = item.Year;
                int targetM = item.Month;
                slotHighlight.MouseEnter += (s, e) =>
                {
                    if (!(targetY == selectedYear && targetM == selectedMonth))
                        slotHighlight.Background = BrushFromHex("#F8FAFC");
                };
                slotHighlight.MouseLeave += (s, e) =>
                {
                    if (!(targetY == selectedYear && targetM == selectedMonth))
                        slotHighlight.Background = Brushes.Transparent;
                };
                slotHighlight.MouseLeftButtonUp += (s, e) =>
                {
                    YearComboBox.SelectionChanged -= Filter_SelectionChanged;
                    MonthComboBox.SelectionChanged -= Filter_SelectionChanged;
                    YearComboBox.SelectedItem = targetY;
                    MonthComboBox.SelectedItem = targetM;
                    YearComboBox.SelectionChanged += Filter_SelectionChanged;
                    MonthComboBox.SelectionChanged += Filter_SelectionChanged;
                    Filter_SelectionChanged(this, null!);
                };

                Canvas.SetLeft(slotHighlight, slotLeft + 3);
                Canvas.SetTop(slotHighlight, 4);
                IncomeTrendCanvas.Children.Add(slotHighlight);

                // Sloupec Příjmů
                double incHeight = Math.Max(4, ((double)item.Income / maxVal) * chartHeight);
                Rectangle incBar = new Rectangle
                {
                    Width = singleBarWidth,
                    Height = incHeight,
                    Fill = incBrush,
                    RadiusX = 4,
                    RadiusY = 4,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(incBar, centerX - singleBarWidth - barGap / 2.0);
                Canvas.SetTop(incBar, chartTop + chartHeight - incHeight);
                IncomeTrendCanvas.Children.Add(incBar);

                // Sloupec Nákladů
                double expHeight = Math.Max(4, ((double)item.Expense / maxVal) * chartHeight);
                Rectangle expBar = new Rectangle
                {
                    Width = singleBarWidth,
                    Height = expHeight,
                    Fill = expBrush,
                    RadiusX = 4,
                    RadiusY = 4,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(expBar, centerX + barGap / 2.0);
                Canvas.SetTop(expBar, chartTop + chartHeight - expHeight);
                IncomeTrendCanvas.Children.Add(expBar);

                // Částka příjmů nad vyšším sloupcem
                double tallerHeight = Math.Max(incHeight, expHeight);
                TextBlock topValText = new TextBlock
                {
                    Text = $"{(item.Income / 1000m):N0}k",
                    FontSize = 10,
                    FontWeight = isSelectedMonth ? FontWeights.Bold : FontWeights.SemiBold,
                    Foreground = BrushFromHex(isSelectedMonth ? "#1D4ED8" : "#64748B"),
                    Width = slotWidth,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(topValText, slotLeft);
                Canvas.SetTop(topValText, chartTop + chartHeight - tallerHeight - 16);
                IncomeTrendCanvas.Children.Add(topValText);

                // Popisek měsíce
                TextBlock dateText = new TextBlock
                {
                    Text = $"{item.Month:00}/{item.Year % 100:00}",
                    FontSize = 11,
                    FontWeight = isSelectedMonth ? FontWeights.Bold : FontWeights.Medium,
                    Foreground = BrushFromHex(isSelectedMonth ? "#1D4ED8" : "#64748B"),
                    Width = slotWidth,
                    TextAlignment = TextAlignment.Center,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dateText, slotLeft);
                Canvas.SetTop(dateText, chartTop + chartHeight + 8);
                IncomeTrendCanvas.Children.Add(dateText);

                // Bod čisté bilance (mapovaný do rozsahu výšky grafu)
                double clampedNet = Math.Clamp((double)item.Net, 0, maxVal);
                double netY = chartTop + chartHeight - (clampedNet / maxVal) * chartHeight;
                netPoints.Add(new Point(centerX, netY));
            }

            // Vyhlazená křivka čistého cashflow přes měsíce
            if (netPoints.Count >= 2)
            {
                Path netCurve = new Path
                {
                    Data = CreateSmoothSplineGeometry(netPoints, isClosedArea: false, 0),
                    Stroke = BrushFromHex("#3B82F6"),
                    StrokeThickness = 2.2,
                    IsHitTestVisible = false
                };
                IncomeTrendCanvas.Children.Add(netCurve);

                foreach (var pt in netPoints)
                {
                    Ellipse dot = new Ellipse
                    {
                        Width = 6,
                        Height = 6,
                        Fill = Brushes.White,
                        Stroke = BrushFromHex("#3B82F6"),
                        StrokeThickness = 2,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(dot, pt.X - 3);
                    Canvas.SetTop(dot, pt.Y - 3);
                    IncomeTrendCanvas.Children.Add(dot);
                }
            }
        }

        // ==========================================
        // VÝVOJ CELKOVÉHO ZŮSTATKU V ČASE (SPLINE AREA CHART + CROSSHAIR INSPEKTOR)
        // ==========================================
        private void UpdateBalanceChart()
        {
            if (BalanceTrendCanvas == null || CurrencyComboBox == null || CurrencyComboBox.SelectedItem == null || !IsLoaded) return;
            BalanceTrendCanvas.Children.Clear();
            _balancePoints.Clear();

            var targetCurrency = CurrencyComboBox.SelectedItem as Currency;
            if (targetCurrency == null) return;
            decimal targetRate = targetCurrency.ExchangeRate != 0 ? targetCurrency.ExchangeRate : 1m;

            var accountsToProcess = new List<Account>();
            if (BalanceAccountFilter.SelectedIndex <= 0)
                accountsToProcess = _cachedAccounts;
            else
            {
                string accName = BalanceAccountFilter.SelectedItem?.ToString() ?? "";
                var acc = _cachedAccounts.FirstOrDefault(a => a.Name == accName);
                if (acc != null) accountsToProcess.Add(acc);
            }
            if (!accountsToProcess.Any()) return;

            // Určení koncového data podle nejnovější transakce nebo dneška
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

            var rawData = new List<(DateTime Date, decimal Balance)>();
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
                rawData.Add((current, runningBalance));
                current = next;
            }

            if (rawData.Count < 2) return;

            decimal startBal = rawData.First().Balance;
            decimal endBal = rawData.Last().Balance;
            decimal minBal = rawData.Min(d => d.Balance);
            decimal maxBal = rawData.Max(d => d.Balance);
            decimal totalDelta = endBal - startBal;

            // Aktualizace horního statistického pásu
            BalanceStartStatText.Text = $"{startBal:N2} {targetCurrency.Code}";
            BalanceMinStatText.Text = $"{minBal:N2} {targetCurrency.Code}";
            BalanceMaxStatText.Text = $"{maxBal:N2} {targetCurrency.Code}";
            BalanceEndStatText.Text = $"{endBal:N2} {targetCurrency.Code} ({(totalDelta >= 0 ? "+" : "")}{totalDelta:N0})";
            BalanceEndStatText.Foreground = BrushFromHex(totalDelta >= 0 ? "#2563EB" : "#DC2626");

            double totalWidth = BalanceTrendCanvas.ActualWidth > 100 ? BalanceTrendCanvas.ActualWidth : 1040;
            double totalHeight = BalanceTrendCanvas.ActualHeight > 80 ? BalanceTrendCanvas.ActualHeight : 260;

            double leftPad = 95; // Prostor pro popisky osy Y vlevo (aby nekolidovaly s křivkou)
            double rightPad = 16;
            double topPad = 14;
            double bottomPad = 10;

            double plotWidth = Math.Max(100, totalWidth - leftPad - rightPad);
            double plotHeight = Math.Max(80, totalHeight - topPad - bottomPad);

            double minB = (double)minBal;
            double maxB = (double)maxBal;
            if (Math.Abs(maxB - minB) < 0.01) { minB -= 100; maxB += 100; }

            double paddingRange = (maxB - minB) * 0.12;
            double lowerBound = minB - paddingRange;
            double upperBound = maxB + paddingRange;
            double range = upperBound - lowerBound;
            if (range <= 0) range = 1;

            // Horizontální mřížka + popisky osy Y
            int gridSteps = 4;
            for (int i = 0; i <= gridSteps; i++)
            {
                double gridY = topPad + (plotHeight / gridSteps) * i;
                decimal gridVal = (decimal)(upperBound - (range / gridSteps) * i);

                Line gridLine = new Line
                {
                    X1 = leftPad,
                    Y1 = gridY,
                    X2 = leftPad + plotWidth,
                    Y2 = gridY,
                    Stroke = BrushFromHex(i == gridSteps ? "#CBD5E1" : "#E2E8F0"),
                    StrokeThickness = 1,
                    StrokeDashArray = i == gridSteps ? null : new DoubleCollection(new double[] { 4, 4 })
                };
                BalanceTrendCanvas.Children.Add(gridLine);

                TextBlock lbl = new TextBlock
                {
                    Text = $"{gridVal:N0} {targetCurrency.Code}",
                    FontSize = 11,
                    FontWeight = FontWeights.Medium,
                    Foreground = BrushFromHex("#64748B"),
                    Width = leftPad - 12,
                    TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(lbl, 0);
                Canvas.SetTop(lbl, gridY - 8);
                BalanceTrendCanvas.Children.Add(lbl);
            }

            var splinePoints = new List<Point>();
            for (int i = 0; i < rawData.Count; i++)
            {
                double x = leftPad + (plotWidth / (rawData.Count - 1)) * i;
                double y = topPad + plotHeight - (((double)rawData[i].Balance - lowerBound) / range * plotHeight);
                Point pt = new Point(x, y);
                splinePoints.Add(pt);

                decimal prevBal = i > 0 ? rawData[i - 1].Balance : rawData[i].Balance;
                _balancePoints.Add(new BalanceDataPoint
                {
                    CanvasPoint = pt,
                    Date = rawData[i].Date,
                    Balance = rawData[i].Balance,
                    DeltaFromPrevious = rawData[i].Balance - prevBal,
                    CurrencyCode = targetCurrency.Code
                });
            }

            // 1. Vyhlazená gradientní výplň pod křivkou (Spline Area)
            Path areaPath = new Path
            {
                Data = CreateSmoothSplineGeometry(splinePoints, isClosedArea: true, topPad + plotHeight),
                Fill = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop((Color)ColorConverter.ConvertFromString("#553B82F6"), 0.0),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#183B82F6"), 0.65),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#003B82F6"), 1.0)
                    },
                    new Point(0, 0), new Point(0, 1)),
                IsHitTestVisible = false
            };
            BalanceTrendCanvas.Children.Add(areaPath);

            // 2. Hlavní vyhlazená Bézierova křivka zůstatku
            Path splineLine = new Path
            {
                Data = CreateSmoothSplineGeometry(splinePoints, isClosedArea: false, 0),
                Stroke = BrushFromHex("#2563EB"),
                StrokeThickness = 3,
                IsHitTestVisible = false
            };
            BalanceTrendCanvas.Children.Add(splineLine);

            // 3. Jemné datové body (pokud jich není příliš mnoho)
            int markerStep = Math.Max(1, rawData.Count / 32);
            for (int i = 0; i < _balancePoints.Count; i += markerStep)
            {
                var bp = _balancePoints[i];
                Ellipse dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Brushes.White,
                    Stroke = BrushFromHex("#2563EB"),
                    StrokeThickness = 2,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, bp.CanvasPoint.X - 3);
                Canvas.SetTop(dot, bp.CanvasPoint.Y - 3);
                BalanceTrendCanvas.Children.Add(dot);
            }

            // 4. Popisky osy X (rozložené rovnoměrně po ose X)
            int xLabelCount = Math.Min(6, rawData.Count);
            for (int k = 0; k < xLabelCount; k++)
            {
                int idx = (int)Math.Round(k * (rawData.Count - 1) / (double)Math.Max(1, xLabelCount - 1));
                var bp = _balancePoints[idx];

                TextBlock xLbl = new TextBlock
                {
                    Text = bp.Date.ToString("dd. MM. yyyy"),
                    Foreground = BrushFromHex("#64748B"),
                    FontSize = 11,
                    FontWeight = FontWeights.Medium
                };

                double offset = k == 0 ? 0 : (k == xLabelCount - 1 ? -68 : -34);
                Canvas.SetLeft(xLbl, bp.CanvasPoint.X + offset);
                Canvas.SetTop(xLbl, topPad + plotHeight + 8);
                BalanceTrendCanvas.Children.Add(xLbl);
            }

            // 5. Inicializace interaktivního Crosshair ukazatele
            InitBalanceHoverElements(topPad, topPad + plotHeight);
        }

        private void InitBalanceHoverElements(double topY, double bottomY)
        {
            _hoverVerticalLine = new Line
            {
                Y1 = topY,
                Y2 = bottomY,
                Stroke = BrushFromHex("#94A3B8"),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection(new double[] { 3, 3 }),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            BalanceTrendCanvas.Children.Add(_hoverVerticalLine);

            _hoverOuterMarker = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = BrushFromHex("#333B82F6"),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            BalanceTrendCanvas.Children.Add(_hoverOuterMarker);

            _hoverInnerMarker = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = BrushFromHex("#2563EB"),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            BalanceTrendCanvas.Children.Add(_hoverInnerMarker);

            StackPanel tooltipStack = new StackPanel();
            _hoverDateText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#94A3B8")
            };
            _hoverAmountText = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 2, 0, 2)
            };
            _hoverDeltaText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushFromHex("#38BDF8")
            };

            tooltipStack.Children.Add(_hoverDateText);
            tooltipStack.Children.Add(_hoverAmountText);
            tooltipStack.Children.Add(_hoverDeltaText);

            _hoverTooltipCard = new Border
            {
                Background = BrushFromHex("#0F172A"),
                BorderBrush = BrushFromHex("#334155"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Child = tooltipStack,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.25, Color = Colors.Black }
            };
            BalanceTrendCanvas.Children.Add(_hoverTooltipCard);
        }

        private void BalanceTrendCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_balancePoints.Count == 0 || _hoverVerticalLine == null || _hoverOuterMarker == null || _hoverInnerMarker == null || _hoverTooltipCard == null)
                return;

            Point mousePos = e.GetPosition(BalanceTrendCanvas);

            // Najdeme nejbližší datový bod podle souřadnice X
            BalanceDataPoint nearest = _balancePoints
                .OrderBy(p => Math.Abs(p.CanvasPoint.X - mousePos.X))
                .First();

            _hoverVerticalLine.X1 = nearest.CanvasPoint.X;
            _hoverVerticalLine.X2 = nearest.CanvasPoint.X;
            _hoverVerticalLine.Visibility = Visibility.Visible;

            Canvas.SetLeft(_hoverOuterMarker, nearest.CanvasPoint.X - 8);
            Canvas.SetTop(_hoverOuterMarker, nearest.CanvasPoint.Y - 8);
            _hoverOuterMarker.Visibility = Visibility.Visible;

            Canvas.SetLeft(_hoverInnerMarker, nearest.CanvasPoint.X - 4.5);
            Canvas.SetTop(_hoverInnerMarker, nearest.CanvasPoint.Y - 4.5);
            _hoverInnerMarker.Visibility = Visibility.Visible;

            _hoverDateText!.Text = nearest.Date.ToString("dddd dd. MM. yyyy");
            _hoverAmountText!.Text = $"{nearest.Balance:N2} {nearest.CurrencyCode}";

            if (nearest.DeltaFromPrevious == 0)
            {
                _hoverDeltaText!.Text = "Beze změny oproti předchozímu bodu";
                _hoverDeltaText.Foreground = BrushFromHex("#94A3B8");
            }
            else
            {
                string sign = nearest.DeltaFromPrevious > 0 ? "+" : "";
                _hoverDeltaText!.Text = $"Změna: {sign}{nearest.DeltaFromPrevious:N2} {nearest.CurrencyCode}";
                _hoverDeltaText.Foreground = BrushFromHex(nearest.DeltaFromPrevious > 0 ? "#34D399" : "#F87171");
            }

            _hoverTooltipCard.Visibility = Visibility.Visible;
            _hoverTooltipCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double cardWidth = _hoverTooltipCard.DesiredSize.Width > 0 ? _hoverTooltipCard.DesiredSize.Width : 165;
            double cardHeight = _hoverTooltipCard.DesiredSize.Height > 0 ? _hoverTooltipCard.DesiredSize.Height : 68;

            double cardX = nearest.CanvasPoint.X + 14;
            if (cardX + cardWidth > BalanceTrendCanvas.ActualWidth - 10)
                cardX = nearest.CanvasPoint.X - cardWidth - 14;

            double cardY = Math.Clamp(nearest.CanvasPoint.Y - cardHeight / 2.0, 6, Math.Max(6, BalanceTrendCanvas.ActualHeight - cardHeight - 10));

            Canvas.SetLeft(_hoverTooltipCard, cardX);
            Canvas.SetTop(_hoverTooltipCard, cardY);
        }

        private void BalanceTrendCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_hoverVerticalLine != null) _hoverVerticalLine.Visibility = Visibility.Collapsed;
            if (_hoverOuterMarker != null) _hoverOuterMarker.Visibility = Visibility.Collapsed;
            if (_hoverInnerMarker != null) _hoverInnerMarker.Visibility = Visibility.Collapsed;
            if (_hoverTooltipCard != null) _hoverTooltipCard.Visibility = Visibility.Collapsed;
        }

        // ==========================================
        // POMOCNÉ METODY PRO VYHLAZENÉ KŘIVKY A BARVY
        // ==========================================
        private static PathGeometry CreateSmoothSplineGeometry(List<Point> points, bool isClosedArea, double baselineY)
        {
            PathGeometry geometry = new PathGeometry();
            if (points == null || points.Count == 0) return geometry;

            PathFigure figure = new PathFigure
            {
                StartPoint = points[0],
                IsClosed = isClosedArea
            };

            if (points.Count == 1)
            {
                figure.Segments.Add(new LineSegment(points[0], true));
            }
            else
            {
                double tension = 0.22; // Jemné vyhlazení bez přestřelování extrémů
                for (int i = 0; i < points.Count - 1; i++)
                {
                    Point p0 = i == 0 ? points[0] : points[i - 1];
                    Point p1 = points[i];
                    Point p2 = points[i + 1];
                    Point p3 = (i + 2 < points.Count) ? points[i + 2] : p2;

                    Point cp1 = new Point(
                        p1.X + (p2.X - p0.X) * tension,
                        p1.Y + (p2.Y - p0.Y) * tension);

                    Point cp2 = new Point(
                        p2.X - (p3.X - p1.X) * tension,
                        p2.Y - (p3.Y - p1.Y) * tension);

                    figure.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
                }
            }

            if (isClosedArea && points.Count >= 2)
            {
                figure.Segments.Add(new LineSegment(new Point(points.Last().X, baselineY), false));
                figure.Segments.Add(new LineSegment(new Point(points.First().X, baselineY), false));
            }

            geometry.Figures.Add(figure);
            return geometry;
        }

        private static SolidColorBrush BrushFromHex(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    public class ChartTransactionItem
    {
        public Category Category { get; set; } = null!;
        public TransactionType Type { get; set; }
        public decimal ConvertedAmount { get; set; }
    }

    public class CategoryChartItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public int Count { get; set; }
    }

    public class BalanceDataPoint
    {
        public Point CanvasPoint { get; set; }
        public DateTime Date { get; set; }
        public decimal Balance { get; set; }
        public decimal DeltaFromPrevious { get; set; }
        public string CurrencyCode { get; set; } = "CZK";
    }
}