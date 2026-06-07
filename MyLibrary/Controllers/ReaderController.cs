using MyLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using MyLibrary.Models.ViewModels;
namespace MyLibrary.Controllers
{
    [Authorize]
    public class ReaderController : Controller
    {
        private LibraryDataContext db = new LibraryDataContext();

        // ── 1. MY BORROWS HISTORY & STATUS OVERVIEW ──
        public ActionResult MyBorrows()
        {
            int userId = int.Parse(User.Identity.Name);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            if (reader == null) return RedirectToAction("Index", "Guest");

            var borrowings = db.Borrowings
                .Where(b => b.ReaderId == reader.ReaderId)
                .OrderByDescending(b => b.BorrowDate)
                .ToList();

            List<ReaderBorrowHistoryItem> vm = borrowings.Select(b =>
            {
                var details = db.BorrowingDetails.Where(d => d.BorrowingId == b.BorrowingId).ToList();
                var bookItems = details.Select(d =>
                {
                    var book = db.Books.FirstOrDefault(bk => bk.BookId == d.BookId);
                    var cover = book != null ? db.BookImages
                        .Where(i => i.BookId == book.BookId && i.IsPrimary)
                        .Select(i => i.ImageUrl).FirstOrDefault() : null;

                    return new ReaderBorrowBookItem
                    {
                        BookId = d.BookId,
                        Title = book?.Title ?? "Unknown",
                        CoverUrl = cover,
                        Condition = d.Condition,
                        ReturnedAt = d.ReturnedAt
                    };
                }).ToList();

                return new ReaderBorrowHistoryItem
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

        // ── 2. PRE-FILLED BORROW BASKET FORM VIEW ──
        public ActionResult BorrowForm()
        {
            int userId = int.Parse(User.Identity.Name);

            var userAccount = db.UserAccounts.FirstOrDefault(u => u.UserId == userId);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);

            if (userAccount == null || reader == null)
                return RedirectToAction("Index", "Guest");

            var model = new ReaderBorrowFormViewModel
            {
                ReaderId = reader.ReaderId,
                ReaderCode = reader.ReaderCode,
                FullName = userAccount.FullName,
                Email = userAccount.Email,
                Phone = userAccount.Phone,
                BorrowDate = DateTime.Now,
                DueDate = DateTime.Now.AddDays(14), // Added missing comma here 🚀
                AvatarUrl = userAccount.AvatarUrl
            };

            return View(model);
        }

        // ── 3. PROCESS MULTIPLE ITEMS SIMULTANEOUSLY ──
        [HttpPost]
        public ActionResult ConfirmBorrow(BorrowRequest request)
        {
            var bookIds = request?.BookIds;

            if (bookIds == null || !bookIds.Any())
                return Json(new { success = false, message = "Your selection is empty." });

            if (bookIds.Count > 3)
                return Json(new { success = false, message = "Maximum 3 books allowed at a time." });

            int userId = int.Parse(User.Identity.Name);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            if (reader == null) return Json(new { success = false, message = "Reader profile not found." });

            // Change this line if you only want "Approved"/"Pending" statuses to count as actively unreturned:
            bool hasUnreturned = db.Borrowings
                .Any(b => b.ReaderId == reader.ReaderId &&
                          (b.Status == "Pending" || b.Status == "Approved" || b.Status == "Accepted" || b.Status == "Overdue"));

            if (hasUnreturned)
                return Json(new { success = false, message = "Please return all current books before borrowing more." });

            bool hasPending = db.Borrowings
                .Any(b => b.ReaderId == reader.ReaderId && b.Status == "Pending");

            if (hasPending)
                return Json(new { success = false, message = "You already have a pending request awaiting librarian approval." });

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

            var addedTitles = new List<string>();
            foreach (var id in bookIds)
            {
                var book = db.Books.FirstOrDefault(b => b.BookId == id && b.IsActive);
                if (book == null || book.AvailableCopies <= 0) continue;

                db.BorrowingDetails.InsertOnSubmit(new BorrowingDetail
                {
                    BorrowingId = borrowing.BorrowingId,
                    BookId = id
                });
                addedTitles.Add(book.Title);
            }

            if (!addedTitles.Any())
            {
                db.Borrowings.DeleteOnSubmit(borrowing);
                db.SubmitChanges();
                return Json(new { success = false, message = "None of the selected books are available." });
            }

            db.SubmitChanges();
            return Json(new { success = true, message = "Request sent for " + addedTitles.Count + " book(s). Due in 14 days." });
        }
        // ── 4. LIVE RETURN SYSTEM INTERACTION ──
        [HttpPost]
        public ActionResult ReturnBook(int borrowingId, int bookId)
        {
            var detail = db.BorrowingDetails.FirstOrDefault(d => d.BorrowingId == borrowingId && d.BookId == bookId);
            if (detail == null) return Json(new { success = false, message = "Borrowing record entry not found." });
            if (detail.ReturnedAt != null) return Json(new { success = false, message = "This book has already been marked returned." });

            detail.ReturnedAt = DateTime.Now;
            db.SubmitChanges();

            var siblings = db.BorrowingDetails.Where(d => d.BorrowingId == borrowingId).ToList();
            bool allReturned = siblings.All(s => s.ReturnedAt != null);

            if (allReturned)
            {
                var parent = db.Borrowings.FirstOrDefault(b => b.BorrowingId == borrowingId);
                if (parent != null)
                {
                    parent.ReturnDate = DateTime.Now;
                    parent.Status = "Returned";
                    parent.UpdatedAt = DateTime.Now;
                }
                db.SubmitChanges();
            }

            return Json(new { success = true, message = "Item returned successfully!" });
        }

        // ── 5. NOTIFICATIONS ──
        public ActionResult Notifications()
        {
            int userId = int.Parse(User.Identity.Name);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);

            var notes = new List<ReaderNotificationItem>();

            if (reader != null)
            {
                var dbNotifs = db.EmailNotifications
                    .Where(n => n.UserId == userId && n.Status == "Sent")
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(5).ToList();

                foreach (var n in dbNotifs)
                {
                    notes.Add(new ReaderNotificationItem
                    {
                        Type = n.NotificationType == "FineNotification" ? "fine" : "new",
                        Message = n.Subject + ": " + (n.Body.Length > 80 ? n.Body.Substring(0, 80) + "..." : n.Body),
                        Icon = n.NotificationType == "FineNotification" ? "💰" : "📚"
                    });
                }
            }

            return PartialView("_Notifications", notes);
        }
        [HttpGet]
        public JsonResult GetBorrowBasketBooks(List<int> ids)
        {
            if (ids == null || !ids.Any())
                return Json(new object[] { }, JsonRequestBehavior.AllowGet);

            var books = ids.Select(id =>
            {
                var book = db.Books.FirstOrDefault(b =>
                    b.BookId == id &&
                    b.IsActive);

                if (book == null)
                    return null;

                var cover = db.BookImages
                    .Where(i =>
                        i.BookId == book.BookId &&
                        i.IsPrimary)
                    .Select(i => i.ImageUrl)
                    .FirstOrDefault();

                return new
                {
                    BookId = book.BookId,
                    Title = book.Title,
                    CoverUrl = cover,
                    AvailableCopies = book.AvailableCopies
                };
            })
            .Where(x => x != null)
            .ToList();

            return Json(books, JsonRequestBehavior.AllowGet);
        }
        // ── 6. MY FINES MANAGEMENT SYSTEM ──
        // ── 6. MY FINES MANAGEMENT SYSTEM ──
        [HttpGet]
        public ActionResult MyFines()
        {
            // 1. Resolve user mapping context safely
            int userId = int.Parse(User.Identity.Name);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            if (reader == null) return RedirectToAction("Index", "Guest");

            // 2. Fetch outstanding/unpaid fines using your actual column 'PaymentStatus'
            var unpaidFines = db.Fines
                .Where(f => f.ReaderId == reader.ReaderId && f.PaymentStatus == "Unpaid")
                .OrderByDescending(f => f.IssuedDate)
                .ToList();

            // 3. Map database items into your ViewModels layout
            var fineItems = unpaidFines.Select(f => {
                var detail = db.BorrowingDetails.FirstOrDefault(d => d.BorrowingId == f.BorrowingId);
                var book = detail != null ? db.Books.FirstOrDefault(b => b.BookId == detail.BookId) : null;

                // ── FIX: Read the actual FineType and Notes from the database ──
                string infractionReason = "Overdue Book Return";

                if (!string.IsNullOrEmpty(f.FineType))
                {
                    if (f.FineType == "Damaged")
                    {
                        // Use the notes from SQL if available (e.g., "Pages torn")
                        infractionReason = !string.IsNullOrEmpty(f.Notes) ? $"Damaged ({f.Notes})" : "Damaged / Loose Copy Asset Deficit";
                    }
                    else if (f.FineType == "Lost")
                    {
                        infractionReason = !string.IsNullOrEmpty(f.Notes) ? $"Lost ({f.Notes})" : "Book reported lost";
                    }
                    else if (f.FineType == "Overdue")
                    {
                        infractionReason = "Overdue Book Return";
                    }
                }

                return new FineDetailItem
                {
                    FineId = f.FineId.ToString(),
                    IssuedDate = f.IssuedDate,
                    Amount = f.Amount,
                    Reason = infractionReason, // Now holds your real dynamic reason!
                    BookTitle = book?.Title ?? "General Account Penalty"
                };
            }).ToList();

            // 4. Construct complete parent view configuration model container
            var viewModel = new ReaderFinesViewModel
            {
                FinesList = fineItems,
                TotalFineAmount = fineItems.Sum(f => f.Amount)
            };

            return View(viewModel);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}