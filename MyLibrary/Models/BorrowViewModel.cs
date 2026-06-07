using System;
using System.Collections.Generic;

namespace MyLibrary.Models
{
    // Renamed with 'Reader' prefix to guarantee no conflicts with Librarian view models
    public class ReaderBorrowFormViewModel
    {
        public int ReaderId { get; set; }
        public string ReaderCode { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public DateTime BorrowDate { get; set; }
        public DateTime DueDate { get; set; }
        public string AvatarUrl { get; set; }
    }

    public class ReaderBorrowHistoryItem
    {
        public int BorrowingId { get; set; }
        public DateTime BorrowDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public string Status { get; set; }
        public List<ReaderBorrowBookItem> Books { get; set; } = new List<ReaderBorrowBookItem>();
    }

    public class ReaderBorrowBookItem
    {
        public int BookId { get; set; }
        public string Title { get; set; }
        public string CoverUrl { get; set; }
        public string Condition { get; set; }
        public DateTime? ReturnedAt { get; set; }
    }

    public class ReaderNotificationItem
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string Icon { get; set; }
    }
}