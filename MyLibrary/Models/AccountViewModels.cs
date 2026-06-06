using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MyLibrary.Models.ViewModels
{
    public class LoginViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        [Required]
        public string FullName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string Password { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; }

        public string Phone { get; set; }
    }

    public class ProfileViewModel
    {
        public int UserId { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }

        [Required]
        public string FullName { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string AvatarUrl { get; set; }
        public string Gender { get; set; }
        public DateTime? DateOfBirth { get; set; }

        // Reader fields
        public string ReaderCode { get; set; }
        public DateTime? MembershipDate { get; set; }
        public DateTime? MembershipExpiry { get; set; }
        public int TotalBorrowed { get; set; }
        public decimal TotalFines { get; set; }

        // Librarian fields
        public string LibrarianCode { get; set; }
        public string Department { get; set; }
        public DateTime? HireDate { get; set; }
    }

    public class BorrowHistoryItem
    {
        public int BorrowingId { get; set; }
        public DateTime BorrowDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public string Status { get; set; }
        public System.Collections.Generic.List<BorrowBookItem> Books { get; set; }
    }

    public class BorrowBookItem
    {
        public int BookId { get; set; }
        public string Title { get; set; }
        public string CoverUrl { get; set; }
        public string Condition { get; set; }
        public DateTime? ReturnedAt { get; set; }
    }
    public class LibrarianDashboardViewModel
    {
        public int TotalBooks { get; set; }
        public int TotalReaders { get; set; }
        public int PendingRequests { get; set; }
        public int ActiveBorrowings { get; set; }
        public List<BorrowActivityItem> RecentActivities { get; set; }
    }

    public class BorrowActivityItem
    {
        public int BorrowingId { get; set; }
        public int ReaderId { get; set; }
        public string ReaderName { get; set; }
        public string Status { get; set; }
        public DateTime BorrowDate { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? ReturnDate { get; set; }
        public List<BorrowBookItem> Books { get; set; }
    }
    public class FineManagementViewModel
    {
        public List<FineItem> Fines { get; set; }
        public List<BorrowActivityItem> OverdueBorrows { get; set; }
    }
    public class ReaderDropdownItem
    {
        public int ReaderId { get; set; }
        public string FullName { get; set; }
    }
    public class FineItem
    {
        public int FineId { get; set; }
        public string ReaderName { get; set; }
        public string FineType { get; set; }
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public string PaymentStatus { get; set; }
        public DateTime IssuedDate { get; set; }
        public int BorrowingId { get; set; }
        public int ReaderId { get; set; }
    }
    public class NotificationItem
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string Icon { get; set; }
    }
}   