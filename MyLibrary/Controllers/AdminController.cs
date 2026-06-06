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
    public class AdminController : Controller
    {
        private LibraryDataContext db = new LibraryDataContext();

        private bool IsAdmin()
        {
            if (!Request.IsAuthenticated)
                return false;

            int userId;

            if (!int.TryParse(User.Identity.Name, out userId))
                return false;

            var user = db.UserAccounts.FirstOrDefault(u =>
                u.UserId == userId &&
                u.IsActive &&
                u.Role == "Admin");

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

        // ── DASHBOARD ──
        public ActionResult Index()
        {
            if (!IsAdmin()) return RedirectToLogin();

            RefreshSession();

            var vm = new AdminDashboardViewModel
            {
                TotalBooks = db.Books.Count(b => b.IsActive),
                TotalReaders = db.Readers.Count(),
                TotalBorrowings = db.Borrowings.Count(),
                TotalFines = db.Fines.Sum(f => (decimal?)f.Amount) ?? 0,
                PendingFines = db.Fines.Count(f => f.PaymentStatus != "Paid"),
                ActiveBorrowings = db.Borrowings.Count(b => b.Status == "Approved"),
                RecentBorrowings = GetRecentBorrowings(10),
                RecentFines = GetRecentFines(10)
            };

            return View(vm);
        }

        private List<AdminBorrowingItem> GetRecentBorrowings(int count)
        {
            var ids = db.Borrowings
                .OrderByDescending(b => b.CreatedAt)
                .Take(count)
                .Select(b => b.BorrowingId)
                .ToList();

            var result = new List<AdminBorrowingItem>();

            foreach (var id in ids)
            {
                var b = db.Borrowings.FirstOrDefault(x => x.BorrowingId == id);

                if (b == null)
                    continue;

                var reader = db.Readers.FirstOrDefault(r => r.ReaderId == b.ReaderId);
                var user = reader != null ? db.UserAccounts.FirstOrDefault(u => u.UserId == reader.UserId) : null;
                var details = db.BorrowingDetails.Where(d => d.BorrowingId == id).ToList();

                var titles = details
                    .Select(d => db.Books.FirstOrDefault(bk => bk.BookId == d.BookId)?.Title ?? "Unknown")
                    .ToList();

                result.Add(new AdminBorrowingItem
                {
                    BorrowingId = b.BorrowingId,
                    ReaderName = user?.FullName ?? "Unknown",
                    Status = b.Status,
                    BorrowDate = b.BorrowDate,
                    DueDate = b.DueDate,
                    ReturnDate = b.ReturnDate,
                    BookTitles = titles
                });
            }

            return result;
        }

        private List<AdminFineItem> GetRecentFines(int count)
        {
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
                                 f.TransactionId,
                                 f.PaymentDate,
                                 f.PaymentMethod,
                                 FullName = u != null ? u.FullName : "Unknown"
                             })
                             .Take(count)
                             .ToList();

            return baseFines.Select(f => new AdminFineItem
            {
                FineId = f.FineId,
                ReaderName = f.FullName,
                FineType = f.FineType,
                Amount = f.Amount,
                PaidAmount = f.PaidAmount,
                PaymentStatus = f.PaymentStatus,
                IssuedDate = f.IssuedDate,
                BorrowingId = f.BorrowingId,
                ReaderId = f.ReaderId,
                TransactionId = f.TransactionId,
                PaymentDate = f.PaymentDate,
                PaymentMethod = f.PaymentMethod
            }).ToList();
        }

        // ══════════════════════════════════════
        // BOOKS
        // ══════════════════════════════════════
        public ActionResult Books(string q)
        {
            if (!IsAdmin()) return RedirectToLogin();

            var query = db.Books.Where(b => b.IsActive);

            if (!string.IsNullOrEmpty(q))
                query = query.Where(b => b.Title.Contains(q) || b.ISBN.Contains(q));

            var books = (from b in query
                         orderby b.Title
                         let author = b.AuthorId != null
                             ? db.Authors.Where(a => a.AuthorId == b.AuthorId).Select(a => a.FullName).FirstOrDefault()
                             : "Unknown"
                         let cat = b.CategoryId != null
                             ? db.Categories.Where(c => c.CategoryId == b.CategoryId).Select(c => c.CategoryName).FirstOrDefault()
                             : ""
                         let cover = db.BookImages.Where(i => i.BookId == b.BookId && i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                         select new AdminBookItem
                         {
                             BookId = b.BookId,
                             Title = b.Title,
                             ISBN = b.ISBN,
                             AuthorName = author,
                             CategoryName = cat,
                             TotalCopies = b.TotalCopies,
                             AvailableCopies = b.AvailableCopies,
                             CoverUrl = cover,
                             IsActive = b.IsActive
                         }).ToList();

            ViewBag.SearchQuery = q;
            return View(books);
        }

        public ActionResult AddBook()
        {
            if (!IsAdmin()) return RedirectToLogin();

            PopulateBookDropdowns();
            return View(new AdminBookEditViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddBook(AdminBookEditViewModel model)
        {
            if (!IsAdmin()) return RedirectToLogin();

            if (!ModelState.IsValid)
            {
                PopulateBookDropdowns();
                return View(model);
            }

            var book = new Book
            {
                ISBN = model.ISBN,
                Title = model.Title,
                AuthorId = model.AuthorId,
                CategoryId = model.CategoryId,
                PublisherId = model.PublisherId,
                PublishYear = model.PublishYear,
                TotalCopies = model.TotalCopies,
                AvailableCopies = model.TotalCopies,
                Language = model.Language ?? "English",
                PageCount = model.PageCount,
                Description = model.Description,
                ShelfLocation = model.ShelfLocation,
                IsFeatured = model.IsFeatured,
                IsActive = true,
                AverageRating = 0,
                TotalReviews = 0,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            db.Books.InsertOnSubmit(book);
            db.SubmitChanges();

            for (int i = 1; i <= model.TotalCopies; i++)
            {
                db.BookStatus.InsertOnSubmit(new BookStatus
                {
                    BookId = book.BookId,
                    CopyNumber = i,
                    Status = "Available",
                    UpdatedAt = DateTime.Now
                });
            }

            if (!string.IsNullOrEmpty(model.CoverUrl))
            {
                db.BookImages.InsertOnSubmit(new BookImage
                {
                    BookId = book.BookId,
                    ImageUrl = model.CoverUrl,
                    IsPrimary = true,
                    CreatedAt = DateTime.Now
                });
            }

            db.SubmitChanges();

            TempData["Success"] = "Book added successfully.";
            return RedirectToAction("Books");
        }

        public ActionResult EditBook(int id)
        {
            if (!IsAdmin()) return RedirectToLogin();

            var book = db.Books.FirstOrDefault(b => b.BookId == id);

            if (book == null)
                return HttpNotFound();

            var cover = db.BookImages
                .Where(i => i.BookId == id && i.IsPrimary)
                .Select(i => i.ImageUrl)
                .FirstOrDefault();

            var vm = new AdminBookEditViewModel
            {
                BookId = book.BookId,
                ISBN = book.ISBN,
                Title = book.Title,
                AuthorId = book.AuthorId,
                CategoryId = book.CategoryId,
                PublisherId = book.PublisherId,
                PublishYear = book.PublishYear,
                TotalCopies = book.TotalCopies,
                Language = book.Language,
                PageCount = book.PageCount,
                Description = book.Description,
                ShelfLocation = book.ShelfLocation,
                IsFeatured = book.IsFeatured,
                CoverUrl = cover
            };

            PopulateBookDropdowns();
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditBook(AdminBookEditViewModel model)
        {
            if (!IsAdmin()) return RedirectToLogin();

            if (!ModelState.IsValid)
            {
                PopulateBookDropdowns();
                return View(model);
            }

            var book = db.Books.FirstOrDefault(b => b.BookId == model.BookId);

            if (book == null)
                return HttpNotFound();

            book.ISBN = model.ISBN;
            book.Title = model.Title;
            book.AuthorId = model.AuthorId;
            book.CategoryId = model.CategoryId;
            book.PublisherId = model.PublisherId;
            book.PublishYear = model.PublishYear;
            book.TotalCopies = model.TotalCopies;
            book.Language = model.Language;
            book.PageCount = model.PageCount;
            book.Description = model.Description;
            book.ShelfLocation = model.ShelfLocation;
            book.IsFeatured = model.IsFeatured;
            book.UpdatedAt = DateTime.Now;

            if (!string.IsNullOrEmpty(model.CoverUrl))
            {
                var existing = db.BookImages.FirstOrDefault(i =>
                    i.BookId == model.BookId &&
                    i.IsPrimary);

                if (existing != null)
                {
                    existing.ImageUrl = model.CoverUrl;
                }
                else
                {
                    db.BookImages.InsertOnSubmit(new BookImage
                    {
                        BookId = model.BookId,
                        ImageUrl = model.CoverUrl,
                        IsPrimary = true,
                        CreatedAt = DateTime.Now
                    });
                }
            }

            db.SubmitChanges();

            TempData["Success"] = "Book updated.";
            return RedirectToAction("Books");
        }

        [HttpPost]
        public ActionResult DeleteBook(int id)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var book = db.Books.FirstOrDefault(b => b.BookId == id);

            if (book == null)
                return Json(new { success = false, message = "Book not found." });

            book.IsActive = false;
            book.UpdatedAt = DateTime.Now;

            db.SubmitChanges();

            return Json(new { success = true, message = "Book deleted." });
        }

        public ActionResult BookStatusList(int id)
        {
            if (!IsAdmin()) return RedirectToLogin();

            var book = db.Books.FirstOrDefault(b => b.BookId == id);

            if (book == null)
                return HttpNotFound();

            var copies = db.BookStatus
                .Where(s => s.BookId == id)
                .OrderBy(s => s.CopyNumber)
                .ToList();

            ViewBag.BookTitle = book.Title;
            ViewBag.BookId = id;

            return View(copies);
        }

        [HttpPost]
        public ActionResult UpdateCopyStatus(int statusId, string status)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var copy = db.BookStatus.FirstOrDefault(s => s.StatusId == statusId);

            if (copy == null)
                return Json(new { success = false, message = "Copy not found." });

            copy.Status = status;
            copy.UpdatedAt = DateTime.Now;

            db.SubmitChanges();

            return Json(new { success = true });
        }

        private void PopulateBookDropdowns()
        {
            ViewBag.Authors = new SelectList(
                db.Authors.Where(a => a.IsActive).ToList(),
                "AuthorId",
                "FullName");

            ViewBag.Categories = new SelectList(
                db.Categories.Where(c => c.IsActive).ToList(),
                "CategoryId",
                "CategoryName");

            ViewBag.Publishers = new SelectList(
                db.Publishers.Where(p => p.IsActive).ToList(),
                "PublisherId",
                "PublisherName");
        }

        // ══════════════════════════════════════
        // CATEGORIES
        // ══════════════════════════════════════
        public ActionResult Categories()
        {
            if (!IsAdmin()) return RedirectToLogin();

            var cats = db.Categories
                .OrderBy(c => c.CategoryName)
                .ToList();

            return View(cats);
        }

        [HttpPost]
        public ActionResult AddCategory(string name, string description)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            db.Categories.InsertOnSubmit(new Category
            {
                CategoryName = name,
                Description = description,
                IsActive = true,
                CreatedAt = DateTime.Now
            });

            db.SubmitChanges();

            return Json(new { success = true, message = "Category added." });
        }

        [HttpPost]
        public ActionResult EditCategory(int id, string name, string description)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var cat = db.Categories.FirstOrDefault(c => c.CategoryId == id);

            if (cat == null)
                return Json(new { success = false, message = "Category not found." });

            cat.CategoryName = name;
            cat.Description = description;

            db.SubmitChanges();

            return Json(new { success = true, message = "Category updated." });
        }

        [HttpPost]
        public ActionResult DeleteCategory(int id)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var cat = db.Categories.FirstOrDefault(c => c.CategoryId == id);

            if (cat == null)
                return Json(new { success = false, message = "Category not found." });

            cat.IsActive = false;

            db.SubmitChanges();

            return Json(new { success = true, message = "Category deleted." });
        }

        // ══════════════════════════════════════
        // AUTHORS & PUBLISHERS
        // ══════════════════════════════════════
        public ActionResult Authors()
        {
            if (!IsAdmin()) return RedirectToLogin();

            var authors = db.Authors
                .Where(a => a.IsActive)
                .OrderBy(a => a.FullName)
                .ToList();

            var publishers = db.Publishers
                .Where(p => p.IsActive)
                .OrderBy(p => p.PublisherName)
                .ToList();

            ViewBag.Publishers = publishers;

            return View(authors);
        }

        [HttpPost]
        public ActionResult AddAuthor(string fullName, string bio, string nationality)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            db.Authors.InsertOnSubmit(new Author
            {
                FullName = fullName,
                Bio = bio,
                Nationality = nationality,
                IsActive = true,
                CreatedAt = DateTime.Now
            });

            db.SubmitChanges();

            return Json(new { success = true, message = "Author added." });
        }

        [HttpPost]
        public ActionResult EditAuthor(int id, string fullName, string bio, string nationality)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var author = db.Authors.FirstOrDefault(a => a.AuthorId == id);

            if (author == null)
                return Json(new { success = false, message = "Author not found." });

            author.FullName = fullName;
            author.Bio = bio;
            author.Nationality = nationality;

            db.SubmitChanges();

            return Json(new { success = true, message = "Author updated." });
        }

        [HttpPost]
        public ActionResult DeleteAuthor(int id)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var author = db.Authors.FirstOrDefault(a => a.AuthorId == id);

            if (author == null)
                return Json(new { success = false, message = "Author not found." });

            author.IsActive = false;

            db.SubmitChanges();

            return Json(new { success = true, message = "Author deleted." });
        }

        [HttpPost]
        public ActionResult AddPublisher(string name, string address, string email, string phone, string website)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            db.Publishers.InsertOnSubmit(new Publisher
            {
                PublisherName = name,
                Address = address,
                Email = email,
                Phone = phone,
                Website = website,
                IsActive = true,
                CreatedAt = DateTime.Now
            });

            db.SubmitChanges();

            return Json(new { success = true, message = "Publisher added." });
        }

        [HttpPost]
        public ActionResult DeletePublisher(int id)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var pub = db.Publishers.FirstOrDefault(p => p.PublisherId == id);

            if (pub == null)
                return Json(new { success = false, message = "Publisher not found." });

            pub.IsActive = false;

            db.SubmitChanges();

            return Json(new { success = true, message = "Publisher deleted." });
        }

        // ══════════════════════════════════════
        // READERS
        // ══════════════════════════════════════
        public ActionResult Readers(string q)
        {
            if (!IsAdmin()) return RedirectToLogin();

            var baseReaders = (from r in db.Readers
                               join u in db.UserAccounts on r.UserId equals u.UserId
                               where string.IsNullOrEmpty(q) ||
                                     u.FullName.Contains(q) ||
                                     u.Email.Contains(q)
                               orderby u.FullName
                               select new AdminReaderItem
                               {
                                   ReaderId = r.ReaderId,
                                   UserId = u.UserId,
                                   FullName = u.FullName,
                                   Email = u.Email,
                                   Phone = u.Phone,
                                   ReaderCode = r.ReaderCode,
                                   MembershipExpiry = r.MembershipExpiry,
                                   TotalBorrowed = r.TotalBorrowed,
                                   TotalFines = r.TotalFines,
                                   IsActive = u.IsActive
                               }).ToList();

            ViewBag.SearchQuery = q;

            return View(baseReaders);
        }

        [HttpPost]
        public ActionResult ToggleReaderStatus(int userId)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var user = db.UserAccounts.FirstOrDefault(u => u.UserId == userId);

            if (user == null)
                return Json(new { success = false, message = "User not found." });

            user.IsActive = !user.IsActive;
            user.UpdatedAt = DateTime.Now;

            db.SubmitChanges();

            return Json(new
            {
                success = true,
                isActive = user.IsActive,
                message = user.IsActive ? "Account activated." : "Account deactivated."
            });
        }

        // ══════════════════════════════════════
        // BORROWINGS
        // ══════════════════════════════════════
        public ActionResult Borrowings(string status)
        {
            if (!IsAdmin()) return RedirectToLogin();

            ViewBag.CurrentStatus = status ?? "All";

            var query = db.Borrowings.AsQueryable();

            if (!string.IsNullOrEmpty(status) && status != "All")
                query = query.Where(b => b.Status == status);

            var ids = query
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => b.BorrowingId)
                .ToList();

            return View(GetAdminBorrowings(ids));
        }

        private List<AdminBorrowingItem> GetAdminBorrowings(List<int> ids)
        {
            var result = new List<AdminBorrowingItem>();

            foreach (var id in ids)
            {
                var b = db.Borrowings.FirstOrDefault(x => x.BorrowingId == id);

                if (b == null)
                    continue;

                var reader = db.Readers.FirstOrDefault(r => r.ReaderId == b.ReaderId);
                var user = reader != null ? db.UserAccounts.FirstOrDefault(u => u.UserId == reader.UserId) : null;
                var details = db.BorrowingDetails.Where(d => d.BorrowingId == id).ToList();

                var titles = details
                    .Select(d => db.Books.FirstOrDefault(bk => bk.BookId == d.BookId)?.Title ?? "Unknown")
                    .ToList();

                result.Add(new AdminBorrowingItem
                {
                    BorrowingId = b.BorrowingId,
                    ReaderName = user?.FullName ?? "Unknown",
                    Status = b.Status,
                    BorrowDate = b.BorrowDate,
                    DueDate = b.DueDate,
                    ReturnDate = b.ReturnDate,
                    BookTitles = titles
                });
            }

            return result;
        }

        [HttpPost]
        public ActionResult UpdateBorrowingStatus(int borrowingId, string status)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var borrow = db.Borrowings.FirstOrDefault(b => b.BorrowingId == borrowingId);

            if (borrow == null)
                return Json(new { success = false, message = "Borrowing not found." });

            borrow.Status = status;
            borrow.UpdatedAt = DateTime.Now;

            if (status == "Returned")
                borrow.ReturnDate = DateTime.Now;

            db.SubmitChanges();

            return Json(new { success = true, message = "Status updated." });
        }

        // ══════════════════════════════════════
        // FINE PAYMENTS
        // ══════════════════════════════════════
        public ActionResult Fines()
        {
            if (!IsAdmin()) return RedirectToLogin();

            return View(GetRecentFines(200));
        }

        [HttpPost]
        public ActionResult ConfirmPayment(int fineId, string method, string transactionId)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var fine = db.Fines.FirstOrDefault(f => f.FineId == fineId);

            if (fine == null)
                return Json(new { success = false, message = "Fine not found." });

            fine.PaymentStatus = "Paid";
            fine.PaidAmount = fine.Amount;
            fine.PaymentMethod = method;
            fine.TransactionId = transactionId;
            fine.PaymentDate = DateTime.Now;

            db.SubmitChanges();

            return Json(new { success = true, message = "Payment confirmed." });
        }

        // ══════════════════════════════════════
        // REVIEWS
        // ══════════════════════════════════════
        public ActionResult Reviews()
        {
            if (!IsAdmin()) return RedirectToLogin();

            var reviews = (from r in db.Reviews
                           where r.IsVisible
                           orderby r.ReviewDate descending
                           let reader = db.Readers.FirstOrDefault(rd => rd.ReaderId == r.ReaderId)
                           let user = reader != null ? db.UserAccounts.FirstOrDefault(u => u.UserId == reader.UserId) : null
                           let book = db.Books.FirstOrDefault(b => b.BookId == r.BookId)
                           select new AdminReviewItem
                           {
                               ReviewId = r.ReviewId,
                               BookTitle = book != null ? book.Title : "Unknown",
                               ReaderName = user != null ? user.FullName : "Unknown",
                               Rating = r.Rating,
                               Comment = r.Comment,
                               ReviewDate = r.ReviewDate
                           }).ToList();

            return View(reviews);
        }

        [HttpPost]
        public ActionResult DeleteReview(int id)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            var review = db.Reviews.FirstOrDefault(r => r.ReviewId == id);

            if (review == null)
                return Json(new { success = false, message = "Review not found." });

            review.IsVisible = false;

            db.SubmitChanges();

            return Json(new { success = true, message = "Review removed." });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();

            base.Dispose(disposing);
        }
    }
}