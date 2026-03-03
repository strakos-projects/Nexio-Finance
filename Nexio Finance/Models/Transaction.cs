using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;

namespace NexioFinance.Models
{
    public class Transaction
    {
        public int Id { get; set; }

        public DateTime Date { get; set; }

        public decimal Amount { get; set; } 

        public string Description { get; set; } = string.Empty;

        public TransactionType Type { get; set; }

        public int AccountId { get; set; }
        public Account Account { get; set; } = null!;

        public int? CategoryId { get; set; }
        public Category? Category { get; set; }

        public int? LinkedTransactionId { get; set; }
        public Transaction? LinkedTransaction { get; set; }

        public int Year => Date.Year;
        public int Month => Date.Month;
    }
}