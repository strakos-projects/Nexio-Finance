namespace NexioFinance.Models
{
    public class Currency
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public decimal ExchangeRate { get; set; }

        public bool IsMainCurrency { get; set; }
    }
}