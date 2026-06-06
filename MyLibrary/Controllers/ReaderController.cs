using MyLibrary.Models;
using MyLibrary.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace MyLibrary.Controllers
{
    [Authorize]
    public class ReaderController : Controller
    {
        private LibraryDataContext db = new LibraryDataContext();

        // ── MY BORROWS ──
        public ActionResult MyBorrows()
        {
            int userId = int.Parse(User.Identity.Name);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            if (reader == null) return RedirectToAction("Index", "Guest");

            var borrows = (from b in db.Borrowings
                           where b.ReaderId == reader.ReaderId
                           orderby b.BorrowDate descending
                           select b).ToList();

            var vm = borrows.Select(b =>
            {
                var details = db.BorrowingDetails.Where(d => d.BorrowingId == b.BorrowingId).ToList();
                var bookItems = details.Select(d =>
                {
                    var book = db.Books.FirstOrDefault(bk => bk.BookId == d.BookId);
                    var cover = book != null ? db.BookImages
                        .Where(i => i.BookId == book.BookId && i.IsPrimary)
                        .Select(i => i.ImageUrl).FirstOrDefault() : null;
                    return new BorrowBookItem
                    {
                        BookId = d.BookId,
                        Title = book?.Title ?? "Unknown",
                        CoverUrl = cover,
                        Condition = d.Condition,
                        ReturnedAt = d.ReturnedAt
                    };
                }).ToList();

                return new BorrowHistoryItem
                {
                    BorrowingId = b.BorrowingId,
                    BorrowDate = b.BorrowDate,
                    DueDate = b.DueDate,
                    ReturnDate = b.ReturnDate,
                    Status = b.Status,
                    Books = bookItems
                };
            }).ToList();

            return View(vm);
        }

        // ── NOTIFICATIONS (AJAX) ──
        public ActionResult Notifications()
        {
            int userId = int.Parse(User.Identity.Name);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            var notes = new List<NotificationItem>();

            if (reader != null)
            {
                // DB notifications (fines etc)
                var dbNotifs = db.EmailNotifications
                    .Where(n => n.UserId == userId && n.Status == "Sent")
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(5).ToList();

                foreach (var n in dbNotifs)
                {
                    notes.Add(new NotificationItem
                    {
                        Type = n.NotificationType == "FineNotification" ? "fine" : "new",
                        Message = n.Subject + ": " + n.Body.Substring(0, Math.Min(80, n.Body.Length)) + "...",
                        Icon = n.NotificationType == "FineNotification" ? "💰" : "📚"
                    });
                }

                // Almost due
                var almostDue = db.Borrowings
                    .Where(b => b.ReaderId == reader.ReaderId &&
                                (b.Status == "Approved") &&
                                b.ReturnDate == null &&
                                b.DueDate <= DateTime.Now.AddDays(3) &&
                                b.DueDate >= DateTime.Now)
                    .ToList();

                foreach (var borrow in almostDue)
                {
                    var detail = db.BorrowingDetails.FirstOrDefault(d => d.BorrowingId == borrow.BorrowingId);
                    var book = detail != null ? db.Books.FirstOrDefault(b => b.BookId == detail.BookId) : null;
                    notes.Add(new NotificationItem
                    {
                        Type = "due",
                        Message = string.Format("'{0}' is due on {1}!", book?.Title ?? "A book", borrow.DueDate.ToString("MMM dd")),
                        Icon = "⏰"
                    });
                }
            }

            return PartialView("_Notifications", notes);
        }
        // ── BORROW REQUEST ──
        [HttpPost]
        public ActionResult Borrow(int bookId)
        {
            int userId = int.Parse(User.Identity.Name);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            if (reader == null) return Json(new { success = false, message = "Reader profile not found." });

            var book = db.Books.FirstOrDefault(b => b.BookId == bookId && b.IsActive);
            if (book == null) return Json(new { success = false, message = "Book not found." });
            if (book.AvailableCopies <= 0) return Json(new { success = false, message = "No copies available. Would you like to reserve?" });

            // Check if already borrowed
            var existing = db.Borrowings.FirstOrDefault(b =>
                b.ReaderId == reader.ReaderId &&
                (b.Status == "Approved" || b.Status == "Pending"));
            var existingDetails = existing != null
                ? db.BorrowingDetails.Where(d => d.BorrowingId == existing.BorrowingId && d.BookId == bookId).Any()
                : false;
            if (existingDetails) return Json(new { success = false, message = "You already have this book borrowed." });

            var borrowing = new Borrowing
            {
                ReaderId = reader.ReaderId,
                BorrowDate = DateTime.Now,
                DueDate = DateTime.Now.AddDays(14),
                Status = "Pending",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            db.Borrowings.InsertOnSubmit(borrowing);
            db.SubmitChanges();

            var detail = new BorrowingDetail
            {
                BorrowingId = borrowing.BorrowingId,
                BookId = bookId
            };
            db.BorrowingDetails.InsertOnSubmit(detail);
            db.SubmitChanges();

            return Json(new { success = true, message = "Borrow request submitted! Due in 14 days." });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}