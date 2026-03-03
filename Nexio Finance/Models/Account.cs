using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Collections.Generic;

namespace NexioFinance.Models
{
    public class Account
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty; 

        public string Currency { get; set; } = "CZK"; 

        public decimal InitialBalance { get; set; }

        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    }
}