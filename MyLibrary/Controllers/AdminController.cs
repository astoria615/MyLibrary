using MyLibrary.Models;
using MyLibrary.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
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

            DateTime now = DateTime.Now;
            DateTime firstDayThisMonth = new DateTime(now.Year, now.Month, 1);

            DateTime startMonth;

            if (db.Borrowings.Any())
            {
                DateTime firstBorrowDate = db.Borrowings.Min(b => b.BorrowDate);
                startMonth = new DateTime(firstBorrowDate.Year, firstBorrowDate.Month, 1);
            }
            else
            {
                startMonth = firstDayThisMonth;
            }

            var rawBorrowings = db.Borrowings
                .ToList()
                .GroupBy(b => new { b.BorrowDate.Year, b.BorrowDate.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    MonthNumber = g.Key.Month,
                    Count = g.Count()
                })
                .ToList();

            var borrowingChartLabels = new List<string>();
            var borrowingChartData = new List<int>();

            DateTime loopMonth = startMonth;

            while (loopMonth <= firstDayThisMonth)
            {
                var found = rawBorrowings.FirstOrDefault(x =>
                    x.Year == loopMonth.Year &&
                    x.MonthNumber == loopMonth.Month);

                borrowingChartLabels.Add(loopMonth.ToString("MM/yyyy"));
                borrowingChartData.Add(found != null ? found.Count : 0);

                loopMonth = loopMonth.AddMonths(1);
            }

            ViewBag.BorrowingChartLabels = borrowingChartLabels;
            ViewBag.BorrowingChartData = borrowingChartData;

            ViewBag.ActiveReaders = (from r in db.Readers
                                     join u in db.UserAccounts on r.UserId equals u.UserId
                                     where u.IsActive
                                     select r).Count();

            ViewBag.InactiveReaders = (from r in db.Readers
                                       join u in db.UserAccounts on r.UserId equals u.UserId
                                       where !u.IsActive
                                       select r).Count();

            ViewBag.NewReadersThisMonth = db.Readers.Count(r => r.MembershipDate >= firstDayThisMonth);

            var topBorrowedBooks = (from bd in db.BorrowingDetails
                                    join b in db.Books on bd.BookId equals b.BookId
                                    group bd by new { b.BookId, b.Title } into g
                                    orderby g.Count() descending
                                    select new
                                    {
                                        Title = g.Key.Title,
                                        BorrowCount = g.Count()
                                    })
                                    .Take(10)
                                    .ToList();

            ViewBag.TopBorrowedBookLabels = topBorrowedBooks.Select(x => x.Title).ToList();
            ViewBag.TopBorrowedBookData = topBorrowedBooks.Select(x => x.BorrowCount).ToList();

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

            ViewBag.AuthorName = "";
            ViewBag.CategoryName = "";
            ViewBag.PublisherName = "";

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

            string ebookUrl = SaveEbookFile(model.EbookFile);

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
                PreviewContent = model.PreviewContent,
                EbookUrl = !string.IsNullOrEmpty(ebookUrl) ? ebookUrl : model.EbookUrl,
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
            // After db.SubmitChanges() for the new book
            NotifyReadersNewBook(book.BookId, book.CategoryId, book.Title);

            TempData["Success"] = "Book added successfully.";
            return RedirectToAction("Books");
        }
        private void NotifyReadersNewBook(int bookId, int? categoryId, string bookTitle)
        {
            if (categoryId == null) return;

            // Find readers who borrowed books in this category
            var readerIds = (from bd in db.BorrowingDetails
                             join b in db.Books on bd.BookId equals b.BookId
                             join bw in db.Borrowings on bd.BorrowingId equals bw.BorrowingId
                             where b.CategoryId == categoryId
                             select bw.ReaderId).Distinct().ToList();

            foreach (var readerId in readerIds)
            {
                var reader = db.Readers.FirstOrDefault(r => r.ReaderId == readerId);
                if (reader == null) continue;

                var user = db.UserAccounts.FirstOrDefault(u => u.UserId == reader.UserId && u.IsActive);
                if (user == null) continue;

                string subject = "📚 New Book Added — " + bookTitle;
                string body = $"Hello {user.FullName},\n\nA new book matching your reading interests has been added to the library:\n\n\"{bookTitle}\"\n\nLog in to browse and add it to your borrow form.\n\nBest regards,\nMy Library System";

                db.EmailNotifications.InsertOnSubmit(new EmailNotification
                {
                    UserId = user.UserId,
                    NotificationType = "NewBook",
                    Subject = subject,
                    Body = body,
                    Status = "Sent",
                    SentAt = DateTime.Now,
                    CreatedAt = DateTime.Now
                });
            }

            db.SubmitChanges();
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
                CoverUrl = cover,

                PreviewContent = book.PreviewContent,
                EbookUrl = book.EbookUrl
            };

            var author = book.AuthorId != null ? db.Authors.FirstOrDefault(a => a.AuthorId == book.AuthorId) : null;
            var category = book.CategoryId != null ? db.Categories.FirstOrDefault(c => c.CategoryId == book.CategoryId) : null;
            var publisher = book.PublisherId != null ? db.Publishers.FirstOrDefault(p => p.PublisherId == book.PublisherId) : null;

            ViewBag.AuthorName = author != null ? author.FullName : "";
            ViewBag.CategoryName = category != null ? category.CategoryName : "";
            ViewBag.PublisherName = publisher != null ? publisher.PublisherName : "";

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

            book.PreviewContent = model.PreviewContent;

            string ebookUrl = SaveEbookFile(model.EbookFile);

            if (!string.IsNullOrEmpty(ebookUrl))
            {
                book.EbookUrl = ebookUrl;
            }
            else
            {
                book.EbookUrl = model.EbookUrl;
            }

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

            var cats = db.Categories.OrderBy(c => c.CategoryName).ToList();

            var bookCounts = db.Books
                .Where(b => b.IsActive && b.CategoryId != null)
                .GroupBy(b => b.CategoryId)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .ToList();

            ViewBag.BookCounts = bookCounts.ToDictionary(x => x.CategoryId, x => x.Count);

            return View(cats);
        }

        [HttpPost]
        public ActionResult AddCategory(string name, string description)
        {
            if (!IsAdmin())
                return Json(new { success = false, message = "Unauthorized." });

            if (string.IsNullOrWhiteSpace(name))
                return Json(new { success = false, message = "Category name is required." });

            db.Categories.InsertOnSubmit(new Category
            {
                CategoryName = name.Trim(),
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

            if (string.IsNullOrWhiteSpace(name))
                return Json(new { success = false, message = "Category name is required." });

            var cat = db.Categories.FirstOrDefault(c => c.CategoryId == id);

            if (cat == null)
                return Json(new { success = false, message = "Category not found." });

            cat.CategoryName = name.Trim();
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

            // Check if any books use this category
            bool hasBooks = db.Books.Any(b => b.CategoryId == id && b.IsActive);
            if (hasBooks)
                return Json(new { success = false, message = "Cannot delete — books are assigned to this category." });

            db.Categories.DeleteOnSubmit(cat);
            db.SubmitChanges();

            return Json(new { success = true, message = "Category deleted successfully." });
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
            if (!IsAdmin()) return RedirectToAction("Login", "Account");

            var baseReviews = (from r in db.Reviews
                               where r.IsVisible
                               join rd in db.Readers on r.ReaderId equals rd.ReaderId into jr
                               from rd in jr.DefaultIfEmpty()
                               join u in db.UserAccounts on rd.UserId equals u.UserId into ju
                               from u in ju.DefaultIfEmpty()
                               join b in db.Books on r.BookId equals b.BookId into jb
                               from b in jb.DefaultIfEmpty()
                               orderby r.ReviewDate descending
                               select new
                               {
                                   r.ReviewId,
                                   r.Rating,
                                   r.Comment,
                                   r.ReviewDate,
                                   BookTitle = b != null ? b.Title : "Unknown",
                                   ReaderName = u != null ? u.FullName : "Unknown"
                               }).ToList();

            var reviews = baseReviews.Select(r => new AdminReviewItem
            {
                ReviewId = r.ReviewId,
                BookTitle = r.BookTitle,
                ReaderName = r.ReaderName,
                Rating = r.Rating,
                Comment = r.Comment,
                ReviewDate = r.ReviewDate
            }).ToList();

            var stats = new ReviewStatsViewModel
            {
                TotalReviews = reviews.Count,
                AverageRating = reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0,
                Star5 = reviews.Count(r => r.Rating == 5),
                Star4 = reviews.Count(r => r.Rating == 4),
                Star3 = reviews.Count(r => r.Rating == 3),
                Star2 = reviews.Count(r => r.Rating == 2),
                Star1 = reviews.Count(r => r.Rating == 1),
                Reviews = reviews
            };

            return View(stats);
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

        // ── SEARCH AUTHORS ──
        public ActionResult SearchAuthors(string q)
        {
            var results = db.Authors
                .Where(a => a.IsActive && a.FullName.Contains(q))
                .ToList()
                .Select(a => new { AuthorId = a.AuthorId, FullName = a.FullName })
                .Take(8)
                .ToList();

            return Json(results, JsonRequestBehavior.AllowGet);
        }

        public ActionResult SearchCategories(string q)
        {
            var results = db.Categories
                .Where(c => c.IsActive && c.CategoryName.Contains(q))
                .ToList()
                .Select(c => new { CategoryId = c.CategoryId, CategoryName = c.CategoryName })
                .Take(8)
                .ToList();

            return Json(results, JsonRequestBehavior.AllowGet);
        }

        public ActionResult SearchPublishers(string q)
        {
            var results = db.Publishers
                .Where(p => p.IsActive && p.PublisherName.Contains(q))
                .ToList()
                .Select(p => new { PublisherId = p.PublisherId, PublisherName = p.PublisherName })
                .Take(8)
                .ToList();

            return Json(results, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult GetOrCreateAuthor(string name)
        {
            if (!IsAdmin())
                return Json(new { success = false });

            var existing = db.Authors.FirstOrDefault(a => a.FullName.ToLower() == name.ToLower());

            if (existing != null)
                return Json(new { success = true, id = existing.AuthorId, name = existing.FullName });

            var newAuthor = new Author
            {
                FullName = name,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            db.Authors.InsertOnSubmit(newAuthor);
            db.SubmitChanges();

            return Json(new { success = true, id = newAuthor.AuthorId, name = newAuthor.FullName });
        }

        [HttpPost]
        public ActionResult GetOrCreateCategory(string name)
        {
            if (!IsAdmin())
                return Json(new { success = false });

            var existing = db.Categories.FirstOrDefault(c => c.CategoryName.ToLower() == name.ToLower());

            if (existing != null)
                return Json(new { success = true, id = existing.CategoryId, name = existing.CategoryName });

            var newCat = new Category
            {
                CategoryName = name,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            db.Categories.InsertOnSubmit(newCat);
            db.SubmitChanges();

            return Json(new { success = true, id = newCat.CategoryId, name = newCat.CategoryName });
        }

        [HttpPost]
        public ActionResult GetOrCreatePublisher(string name)
        {
            if (!IsAdmin())
                return Json(new { success = false });

            var existing = db.Publishers.FirstOrDefault(p => p.PublisherName.ToLower() == name.ToLower());

            if (existing != null)
                return Json(new { success = true, id = existing.PublisherId, name = existing.PublisherName });

            var newPub = new Publisher
            {
                PublisherName = name,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            db.Publishers.InsertOnSubmit(newPub);
            db.SubmitChanges();

            return Json(new { success = true, id = newPub.PublisherId, name = newPub.PublisherName });
        }

        private string SaveEbookFile(HttpPostedFileBase file)
        {
            if (file == null || file.ContentLength <= 0)
                return null;

            var allowedExtensions = new[] { ".pdf", ".epub" };
            var extension = Path.GetExtension(file.FileName);

            if (string.IsNullOrEmpty(extension))
                return null;

            extension = extension.ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                return null;

            var folderPath = Server.MapPath("~/Uploads/Ebooks/");

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            var safeFileName = Path.GetFileNameWithoutExtension(file.FileName);
            safeFileName = string.Join("_", safeFileName.Split(Path.GetInvalidFileNameChars()));

            var fileName = safeFileName + "_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + extension;
            var fullPath = Path.Combine(folderPath, fileName);

            file.SaveAs(fullPath);

            return "/Uploads/Ebooks/" + fileName;
        }

        // TEMP: Fix Borrowing Year 2024 -> 2026
        public ActionResult FixBorrowingYear()
        {
            if (!IsAdmin()) return RedirectToLogin();

            var borrowings = db.Borrowings.ToList();

            foreach (var b in borrowings)
            {
                if (b.BorrowDate.Year == 2024)
                {
                    b.BorrowDate = ChangeYear(b.BorrowDate, 2026);
                }

                if (b.DueDate.Year == 2024)
                {
                    b.DueDate = ChangeYear(b.DueDate, 2026);
                }

                if (b.ReturnDate != null && b.ReturnDate.Value.Year == 2024)
                {
                    b.ReturnDate = ChangeYear(b.ReturnDate.Value, 2026);
                }

                if (b.CreatedAt.Year == 2024)
                {
                    b.CreatedAt = ChangeYear(b.CreatedAt, 2026);
                }

                b.UpdatedAt = DateTime.Now;
            }

            var details = db.BorrowingDetails.ToList();

            foreach (var d in details)
            {
                if (d.ReturnedAt != null && d.ReturnedAt.Value.Year == 2024)
                {
                    d.ReturnedAt = ChangeYear(d.ReturnedAt.Value, 2026);
                }
            }

            db.SubmitChanges();

            return Content("Done. All borrowing years from 2024 have been changed to 2026.");
        }

        private DateTime ChangeYear(DateTime oldDate, int newYear)
        {
            int day = Math.Min(oldDate.Day, DateTime.DaysInMonth(newYear, oldDate.Month));

            return new DateTime(
                newYear,
                oldDate.Month,
                day,
                oldDate.Hour,
                oldDate.Minute,
                oldDate.Second
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();

            base.Dispose(disposing);
        }
    }
}