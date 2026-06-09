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
        // ── 4. LIVE RETURN SYSTEM INTERACTION WITH REVIEWS ──
        [HttpPost]
        public ActionResult ReturnBook(int borrowingId, int bookId, int? rating, string comment)
        {
            var detail = db.BorrowingDetails.FirstOrDefault(d => d.BorrowingId == borrowingId && d.BookId == bookId);
            if (detail == null) return Json(new { success = false, message = "Borrowing record entry not found." });
            if (detail.ReturnedAt != null) return Json(new { success = false, message = "This book has already been marked returned." });

            int userId = int.Parse(User.Identity.Name);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            if (reader == null) return Json(new { success = false, message = "Reader profile context missing." });

            // 1. Process and save review parameters into the Database if provided
            if (rating.HasValue && !string.IsNullOrWhiteSpace(comment))
            {
                var bookReview = new Review
                {
                    BookId = bookId,
                    ReaderId = reader.ReaderId,
                    Rating = rating.Value,
                    Comment = comment.Trim(),
                    ReviewDate = DateTime.Now,
                    IsVisible = true
                };

                db.Reviews.InsertOnSubmit(bookReview);
            }

            // 2. Complete your original return updates sequence
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

            return Json(new { success = true, message = "Item returned and review recorded successfully!" });
        }

        // ── 5. NOTIFICATIONS ──
        public ActionResult Notifications()
        {
            int userId = int.Parse(User.Identity.Name);
            var notes = new List<NotificationItem>();

            var dbNotifs = db.EmailNotifications
                .Where(n => n.UserId == userId && n.Status == "Sent")
                .OrderByDescending(n => n.CreatedAt)
                .Take(10).ToList();

            foreach (var n in dbNotifs)
            {
                bool isFine = n.NotificationType == "FineNotification";
                bool isNewBook = n.NotificationType == "NewBook";

                notes.Add(new NotificationItem
                {
                    Type = isFine ? "fine" : "new",
                    Icon = isFine ? "💰" : "📚",
                    Message = n.Subject + ": " + (n.Body.Length > 80 ? n.Body.Substring(0, 80) + "..." : n.Body),
                    Url = isFine ? "/Reader/MyFines" : "/Guest/Index"
                });
            }

            // Mark as read — save last seen timestamp to session
            Session["NotifLastSeen"] = DateTime.Now;

            return PartialView("_Notifications", notes);
        }

        // New action to get unread count
        [HttpGet]
        public JsonResult GetUnreadNotifCount()
        {
            int userId = int.Parse(User.Identity.Name);
            DateTime lastSeen = Session["NotifLastSeen"] != null
                ? (DateTime)Session["NotifLastSeen"]
                : DateTime.MinValue;

            int count = db.EmailNotifications
                .Count(n => n.UserId == userId &&
                            n.Status == "Sent" &&
                            n.CreatedAt > lastSeen);

            return Json(new { count = count }, JsonRequestBehavior.AllowGet);
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

            // 2. Fetch ALL historical and current fine records for this reader
            var allFines = db.Fines
                .Where(f => f.ReaderId == reader.ReaderId)
                .OrderByDescending(f => f.IssuedDate)
                .ToList();

            // 3. Map database items into our structured FineDetailItem records
            var mappedItems = allFines.Select(f => {
                var detail = db.BorrowingDetails.FirstOrDefault(d => d.BorrowingId == f.BorrowingId);
                var book = detail != null ? db.Books.FirstOrDefault(b => b.BookId == detail.BookId) : null;

                // Process fine type string safely
                string infractionReason = "Overdue Book Return";
                if (!string.IsNullOrEmpty(f.FineType))
                {
                    if (f.FineType == "Damaged")
                        infractionReason = !string.IsNullOrEmpty(f.Notes) ? $"Damaged ({f.Notes})" : "Damaged Asset Deficit";
                    else if (f.FineType == "Lost")
                        infractionReason = !string.IsNullOrEmpty(f.Notes) ? $"Lost ({f.Notes})" : "Book reported lost";
                }

                return new FineDetailItem
                {
                    FineId = f.FineId.ToString(),
                    IssuedDate = f.IssuedDate,
                    PaymentDate = f.PaymentDate,
                    Amount = f.Amount,
                    PaidAmount = f.PaidAmount,
                    PaymentMethod = f.PaymentMethod ?? "N/A",
                    PaymentStatus = f.PaymentStatus,
                    Reason = infractionReason,
                    BookTitle = book?.Title ?? "General Account Penalty"
                };
            }).ToList();

            // 4. Group into Active vs Settled lists for view presentation layout
            var viewModel = new ReaderFinesViewModel
            {
                ActiveFinesList = mappedItems.Where(f => f.PaymentStatus == "Unpaid").ToList(),
                SettledFinesHistory = mappedItems.Where(f => f.PaymentStatus == "Paid").ToList(),
                TotalFineAmount = mappedItems.Where(f => f.PaymentStatus == "Unpaid").Sum(f => f.Amount)
            };

            return View(viewModel);
        }
        public ActionResult VNPayReturn()
        {
            string vnp_ResponseCode = Request.QueryString["vnp_ResponseCode"];

            if (vnp_ResponseCode == "00")
            {
                int userId = int.Parse(User.Identity.Name);
                var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
                if (reader != null)
                {
                    var unpaidFines = db.Fines
                        .Where(f => f.ReaderId == reader.ReaderId && f.PaymentStatus == "Unpaid")
                        .ToList();

                    foreach (var fine in unpaidFines)
                    {
                        fine.PaymentStatus = "Paid";
                        fine.PaymentDate = DateTime.Now;
                        fine.PaymentMethod = "VNPay";
                        fine.PaidAmount = fine.Amount;
                    }

                    db.SubmitChanges();
                }
            }

            return RedirectToAction("MyFines");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}