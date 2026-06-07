using System.Collections.Generic;

namespace MyLibrary.Models
{
    public class ReaderFinesViewModel
    {
        public decimal TotalFineAmount { get; set; }
        public List<FineDetailItem> ActiveFinesList { get; set; }
        public List<FineDetailItem> SettledFinesHistory { get; set; }
    }

    public class FineDetailItem
    {
        public string FineId { get; set; }
        public System.DateTime IssuedDate { get; set; }
        public System.DateTime? PaymentDate { get; set; } // Nullable since unpaid items won't have this yet
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public string PaymentMethod { get; set; }
        public string PaymentStatus { get; set; }
        public string Reason { get; set; }
        public string BookTitle { get; set; }
    }
}