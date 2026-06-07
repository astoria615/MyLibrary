using MyLibrary.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MyLibrary.Models
{
    public class ReaderProfileViewModel
    {
        public int ReaderId { get; set; }
        public int UserId { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string AvatarUrl { get; set; }
        public decimal TotalFines { get; set; }
        public List<BorrowActivityItem> BorrowingHistory { get; set; }
    }
}