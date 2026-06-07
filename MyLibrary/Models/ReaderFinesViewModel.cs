using System.Collections.Generic;

namespace MyLibrary.Models
{
    public class ReaderFinesViewModel
    {
        public decimal TotalFineAmount { get; set; }
        public List<FineDetailItem> FinesList { get; set; }
    }

    public class FineDetailItem
    {
        public string FineId { get; set; }
        public System.DateTime IssuedDate { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } // Displays: "Overdue" or "Lost Copy"
        public string BookTitle { get; set; }
    }
}