using MyLibrary.Models;
using MyLibrary.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using System.Web.Security;

namespace MyLibrary.Controllers
{
    [Authorize]
    public class LibrarianController : Controller
    {
        private LibraryDataContext db = new LibraryDataContext();

        private bool IsLibrarian()
        {
            if (!Request.IsAuthenticated)
                return false;

            int userId;

            if (!int.TryParse(User.Identity.Name, out userId))
                return false;

            var user = db.UserAccounts.FirstOrDefault(u =>
                u.UserId == userId &&
                u.IsActive &&
                (u.Role == "Librarian" || u.Role == "Admin"));

            return user != null;
        }

        private ActionResult RedirectToLogin()
        {
            FormsAuthentication.SignOut();
            Session.Clear();
            Session.Abandon();

            return RedirectToAction("Login", "Account");
        }

        private void RefreshSession()
        {
            if (Request.IsAuthenticated)
            {
                int userId;

                if (!int.TryParse(User.Identity.Name, out userId))
                    return;

                var user = db.UserAccounts.FirstOrDefault(u => u.UserId == userId && u.IsActive);

                if (user != null)
                {
                    Session["UserId"] = user.UserId;
                    Session["FullName"] = user.FullName;
                    Session["AvatarUrl"] = user.AvatarUrl ?? "";
                    Session["Role"] = user.Role;
                }
            }
        }

        private List<BorrowActivityItem> BuildActivityList(List<int> borrowingIds)
        {
            var result = new List<BorrowActivityItem>();

            foreach (var id in borrowingIds)
            {
                var b = db.Borrowings.FirstOrDefault(x => x.BorrowingId == id);

                if (b == null)
                    continue;

                var reader = db.Readers.FirstOrDefault(r => r.ReaderId == b.ReaderId);
                var user = reader != null ? db.UserAccounts.FirstOrDefault(u => u.UserId == reader.UserId) : null;
                var details = db.BorrowingDetails.Where(d => d.BorrowingId == id).ToList();

                var books = new List<BorrowBookItem>();

                foreach (var d in details)
                {
                    var book = db.Books.FirstOrDefault(bk => bk.BookId == d.BookId);

                    var cover = book != null
                        ? db.BookImages.Where(i => i.BookId == book.BookId && i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                        : null;

                    books.Add(new BorrowBookItem
                    {
                        BookId = d.BookId,
                        Title = book != null ? book.Title : "Unknown",
                        CoverUrl = cover,
                        Condition = d.Condition,
                        ReturnedAt = d.ReturnedAt
                    });
                }

                result.Add(new BorrowActivityItem
                {
                    BorrowingId = b.BorrowingId,
                    ReaderId = reader != null ? reader.ReaderId : 0,
                    ReaderName = user != null ? user.FullName : "Unknown",
                    Status = b.Status,
                    BorrowDate = b.BorrowDate,
                    DueDate = b.DueDate,
                    ReturnDate = b.ReturnDate,
                    Books = books
                });
            }

            return result;
        }

        // ── DASHBOARD ──
        public ActionResult Index()
        {
            if (!IsLibrarian()) return RedirectToLogin();

            RefreshSession();

            var borrowingIds = db.Borrowings
                .OrderByDescending(b => b.CreatedAt)
                .Take(20)
                .Select(b => b.BorrowingId)
                .ToList();

            var vm = new LibrarianDashboardViewModel
            {
                TotalBooks = db.Books.Count(b => b.IsActive),
                TotalReaders = db.Readers.Count(),
                PendingRequests = db.Borrowings.Count(b => b.Status == "Pending"),
                ActiveBorrowings = db.Borrowings.Count(b => b.Status == "Approved"),
                RecentActivities = BuildActivityList(borrowingIds)
            };

            return View(vm);
        }

        // ── BORROW REQUESTS ──
        public ActionResult BorrowRequests(string status)
        {
            if (!IsLibrarian()) return RedirectToLogin();

            if (string.IsNullOrEmpty(status))
                status = "Pending";

            ViewBag.CurrentStatus = status;

            var borrowingIds = db.Borrowings
                .Where(b => b.Status == status)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => b.BorrowingId)
                .ToList();

            return View(BuildActivityList(borrowingIds));
        }

        // ── APPROVE BORROW ──
        [HttpPost]
        public ActionResult ApproveBorrow(int borrowingId)
        {
            if (!IsLibrarian())
                return Json(new { success = false, message = "Unauthorized." });

            int userId = int.Parse(User.Identity.Name);

            var librarian = db.Librarians.FirstOrDefault(l => l.UserId == userId);
            var borrow = db.Borrowings.FirstOrDefault(b => b.BorrowingId == borrowingId);

            if (borrow == null)
                return Json(new { success = false, message = "Not found." });

            borrow.Status = "Approved";
            borrow.LibrarianId = librarian != null ? (int?)librarian.LibrarianId : null;
            borrow.UpdatedAt = DateTime.Now;

            db.SubmitChanges();

            return Json(new { success = true, message = "Borrow request approved." });
        }

        // ── REJECT BORROW ──
        [HttpPost]
        public ActionResult RejectBorrow(int borrowingId)
        {
            if (!IsLibrarian())
                return Json(new { success = false, message = "Unauthorized." });

            var borrow = db.Borrowings.FirstOrDefault(b => b.BorrowingId == borrowingId);

            if (borrow == null)
                return Json(new { success = false, message = "Not found." });

            borrow.Status = "Rejected";
            borrow.UpdatedAt = DateTime.Now;

            db.SubmitChanges();

            return Json(new { success = true, message = "Request rejected." });
        }

        // ── MARK RETURNED ──
        [HttpPost]
        public ActionResult MarkReturned(int borrowingId)
        {
            if (!IsLibrarian())
                return Json(new { success = false, message = "Unauthorized." });

            var borrow = db.Borrowings.FirstOrDefault(b => b.BorrowingId == borrowingId);

            if (borrow == null)
                return Json(new { success = false, message = "Not found." });

            borrow.Status = "Returned";
            borrow.ReturnDate = DateTime.Now;
            borrow.UpdatedAt = DateTime.Now;

            var details = db.BorrowingDetails.Where(d => d.BorrowingId == borrowingId).ToList();

            foreach (var d in details)
            {
                d.ReturnedAt = DateTime.Now;

                var book = db.Books.FirstOrDefault(b => b.BookId == d.BookId);

                if (book != null)
                    book.AvailableCopies++;
            }

            db.SubmitChanges();

            return Json(new { success = true, message = "Marked as returned." });
        }

        // ── FINE MANAGEMENT ──
        public ActionResult FineManagement()
        {
            if (!IsLibrarian()) return RedirectToLogin();

            var baseFines = (from f in db.Fines
                             join r in db.Readers on f.ReaderId equals r.ReaderId into jr
                             from r in jr.DefaultIfEmpty()
                             join u in db.UserAccounts on r.UserId equals u.UserId into ju
                             from u in ju.DefaultIfEmpty()
                             orderby f.IssuedDate descending
                             select new
                             {
                                 f.FineId,
                                 f.FineType,
                                 f.Amount,
                                 f.PaidAmount,
                                 f.PaymentStatus,
                                 f.IssuedDate,
                                 f.BorrowingId,
                                 f.ReaderId,
                                 FullName = u != null ? u.FullName : "Unknown"
                             }).ToList();

            var fines = baseFines.Select(f => new FineItem
            {
                FineId = f.FineId,
                ReaderName = f.FullName,
                FineType = f.FineType,
                Amount = f.Amount,
                PaidAmount = f.PaidAmount,
                PaymentStatus = f.PaymentStatus,
                IssuedDate = f.IssuedDate,
                BorrowingId = f.BorrowingId,
                ReaderId = f.ReaderId
            }).ToList();

            var overdueIds = db.Borrowings
                .Where(b => b.Status == "Overdue" ||
                            (b.ReturnDate == null &&
                             b.DueDate < DateTime.Now &&
                             b.Status == "Approved"))
                .Select(b => b.BorrowingId)
                .ToList();

            var damagedBookIds = db.BookStatus
                .Where(s => s.Status == "Damaged" || s.Status == "Lost")
                .Select(s => s.BookId)
                .ToList();

            var damagedBorrowIds = db.BorrowingDetails
                .Where(d => damagedBookIds.Contains(d.BookId))
                .Select(d => d.BorrowingId)
                .Distinct()
                .ToList();

            var allIssueIds = overdueIds
                .Union(damagedBorrowIds)
                .Distinct()
                .ToList();

            var vm = new FineManagementViewModel
            {
                Fines = fines,
                OverdueBorrows = BuildActivityList(allIssueIds)
            };

            return View(vm);
        }

        // ── ADD FINE ──
        [HttpPost]
        public ActionResult AddFine(int borrowingId, int readerId, string fineType, decimal amount, string notes)
        {
            if (!IsLibrarian())
                return Json(new { success = false, message = "Unauthorized." });

            int userId = int.Parse(User.Identity.Name);

            var librarian = db.Librarians.FirstOrDefault(l => l.UserId == userId);

            var fine = new Fine
            {
                BorrowingId = borrowingId,
                ReaderId = readerId,
                FineType = fineType,
                Amount = amount,
                PaidAmount = 0,
                PaymentStatus = "Unpaid",
                IssuedDate = DateTime.Now,
                ConfirmedBy = librarian != null ? (int?)librarian.LibrarianId : null,
                Notes = notes
            };

            db.Fines.InsertOnSubmit(fine);
            db.SubmitChanges();

            var reader = db.Readers.FirstOrDefault(r => r.ReaderId == readerId);

            if (reader != null)
            {
                reader.TotalFines += amount;
                db.SubmitChanges();
            }

            var readerUser = reader != null
                ? db.UserAccounts.FirstOrDefault(u => u.UserId == reader.UserId)
                : null;

            if (readerUser != null)
            {
                var notification = new EmailNotification
                {
                    UserId = readerUser.UserId,
                    NotificationType = "FineNotification",
                    Subject = "Fine Issued - " + fineType,
                    Body = string.Format(
                        "A {0} fine of ${1:F2} has been issued. Reason: {2}",
                        fineType,
                        amount,
                        notes ?? "N/A"),
                    Status = "Sent",
                    SentAt = DateTime.Now,
                    CreatedAt = DateTime.Now
                };

                db.EmailNotifications.InsertOnSubmit(notification);
                db.SubmitChanges();
            }

            return Json(new { success = true, message = "Fine added successfully." });
        }

        // ── UPDATE PAYMENT STATUS ──
        [HttpPost]
        public ActionResult UpdatePaymentStatus(int fineId, string status)
        {
            if (!IsLibrarian())
                return Json(new { success = false, message = "Unauthorized." });

            var fine = db.Fines.FirstOrDefault(f => f.FineId == fineId);

            if (fine == null)
                return Json(new { success = false, message = "Fine not found." });

            fine.PaymentStatus = status;

            if (status == "Paid")
            {
                fine.PaidAmount = fine.Amount;
                fine.PaymentMethod = "Cash";
                fine.PaymentDate = DateTime.Now;
            }

            db.SubmitChanges();

            return Json(new { success = true, message = "Payment status updated." });
        }

        [HttpPost]
        public ActionResult MarkPaid(int fineId, string paymentMethod, string transactionId, string paymentDate)
        {
            if (!IsLibrarian())
                return Json(new { success = false, message = "Unauthorized." });

            var fine = db.Fines.FirstOrDefault(f => f.FineId == fineId);

            if (fine == null)
                return Json(new { success = false, message = "Fine not found." });

            DateTime parsedDate;

            fine.PaymentStatus = "Paid";
            fine.PaidAmount = fine.Amount;
            fine.PaymentMethod = paymentMethod;
            fine.TransactionId = transactionId;
            fine.PaymentDate = DateTime.TryParse(paymentDate, out parsedDate)
                ? parsedDate
                : DateTime.Now;

            db.SubmitChanges();

            return Json(new { success = true, message = "Fine marked as paid." });
        }

        // ── REPORT ISSUE ──
        public ActionResult ReportIssue()
        {
            if (!IsLibrarian()) return RedirectToLogin();

            var books = db.Books
                .Where(b => b.IsActive)
                .Select(b => new { b.BookId, b.Title })
                .ToList();

            ViewBag.Books = new SelectList(books, "BookId", "Title");

            var readers = (from r in db.Readers
                           join u in db.UserAccounts on r.UserId equals u.UserId
                           select new ReaderDropdownItem
                           {
                               ReaderId = r.ReaderId,
                               FullName = u.FullName
                           }).ToList();

            ViewBag.ReadersList = readers;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ReportIssue(int bookId, int copyNumber, string issueType, string notes, int? readerId)
        {
            if (!IsLibrarian()) return RedirectToLogin();

            var copy = db.BookStatus.FirstOrDefault(s =>
                s.BookId == bookId &&
                s.CopyNumber == copyNumber);

            if (copy != null)
            {
                copy.Status = issueType == "Lost" ? "Lost" : "Damaged";
                copy.Notes = notes;
                copy.UpdatedAt = DateTime.Now;

                db.SubmitChanges();
            }

            var book = db.Books.FirstOrDefault(b => b.BookId == bookId);

            if (book != null && book.AvailableCopies > 0)
            {
                book.AvailableCopies--;
                db.SubmitChanges();
            }

            if (readerId.HasValue && readerId.Value > 0)
            {
                int userId = int.Parse(User.Identity.Name);

                var librarian = db.Librarians.FirstOrDefault(l => l.UserId == userId);

                decimal fineAmount = issueType == "Lost" ? 50 : 20;

                var borrowing = db.Borrowings.FirstOrDefault(b =>
                    b.ReaderId == readerId.Value &&
                    (b.Status == "Approved" || b.Status == "Overdue"));

                if (borrowing != null)
                {
                    var fine = new Fine
                    {
                        BorrowingId = borrowing.BorrowingId,
                        ReaderId = readerId.Value,
                        FineType = issueType == "Lost" ? "Lost" : "Damaged",
                        Amount = fineAmount,
                        PaidAmount = 0,
                        PaymentStatus = "Unpaid",
                        IssuedDate = DateTime.Now,
                        ConfirmedBy = librarian != null ? (int?)librarian.LibrarianId : null,
                        Notes = notes
                    };

                    db.Fines.InsertOnSubmit(fine);
                    db.SubmitChanges();

                    var reader = db.Readers.FirstOrDefault(r => r.ReaderId == readerId.Value);

                    if (reader != null)
                    {
                        reader.TotalFines += fineAmount;
                        db.SubmitChanges();
                    }

                    var readerUser = reader != null
                        ? db.UserAccounts.FirstOrDefault(u => u.UserId == reader.UserId)
                        : null;

                    if (readerUser != null)
                    {
                        db.EmailNotifications.InsertOnSubmit(new EmailNotification
                        {
                            UserId = readerUser.UserId,
                            NotificationType = "FineNotification",
                            Subject = "Fine Issued - Copy " + issueType,
                            Body = string.Format(
                                "A {0} fine of ${1:F2} has been issued. {2}",
                                issueType,
                                fineAmount,
                                notes ?? ""),
                            Status = "Sent",
                            SentAt = DateTime.Now,
                            CreatedAt = DateTime.Now
                        });

                        db.SubmitChanges();
                    }
                }
            }

            TempData["Success"] = "Issue reported successfully.";
            return RedirectToAction("ReportIssue");
        }

        // ── GET COPIES AJAX ──
        public ActionResult GetCopies(int bookId)
        {
            if (!IsLibrarian())
                return Json(new { success = false, message = "Unauthorized." }, JsonRequestBehavior.AllowGet);

            var copies = db.BookStatus
                .Where(s => s.BookId == bookId)
                .OrderBy(s => s.CopyNumber)
                .ToList()
                .Select(s => new
                {
                    copyNumber = s.CopyNumber,
                    status = s.Status
                })
                .ToList();

            return Json(copies, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAllBooks()
        {
            if (!IsLibrarian())
                return Json(new { success = false, message = "Unauthorized." }, JsonRequestBehavior.AllowGet);

            var books = db.Books
                .Where(b => b.IsActive)
                .Select(b => new
                {
                    bookId = b.BookId,
                    title = b.Title
                })
                .ToList();

            return Json(books, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAllReaders()
        {
            if (!IsLibrarian())
                return Json(new { success = false, message = "Unauthorized." }, JsonRequestBehavior.AllowGet);

            var readers = (from r in db.Readers
                           join u in db.UserAccounts on r.UserId equals u.UserId
                           select new
                           {
                               readerId = r.ReaderId,
                               fullName = u.FullName
                           }).ToList();

            return Json(readers, JsonRequestBehavior.AllowGet);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();

            base.Dispose(disposing);
        }
    }
}