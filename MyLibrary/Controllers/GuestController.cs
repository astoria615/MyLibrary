using MyLibrary.Models;
using MyLibrary.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace MyLibrary.Controllers
{
    public class GuestController : Controller
    {
        private LibraryDataContext db = new LibraryDataContext();

        // ─────────────────────────────────────────
        // HOME PAGE
        // ─────────────────────────────────────────
        private void RefreshSession()
        {
            if (Request.IsAuthenticated && Session["FullName"] == null)
            {
                int userId = int.Parse(User.Identity.Name);
                var user = db.UserAccounts.FirstOrDefault(u => u.UserId == userId);

                if (user != null)
                {
                    Session["UserId"] = user.UserId;
                    Session["FullName"] = user.FullName;
                    Session["AvatarUrl"] = user.AvatarUrl ?? "";
                    Session["Role"] = user.Role;
                }
            }
        }

        public ActionResult Index(int? selectedBookId, int? categoryId)
        {
            RefreshSession();

            var categories = db.Categories
                .Where(c => c.IsActive)
                .Select(c => new CategoryItem
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName
                })
                .ToList();

            var newBooks = (from b in db.Books
                            where b.IsActive
                            let cover = db.BookImages
                                .Where(i => i.BookId == b.BookId && i.IsPrimary)
                                .Select(i => i.ImageUrl)
                                .FirstOrDefault()
                            let author = b.AuthorId != null
                                ? db.Authors
                                    .Where(a => a.AuthorId == b.AuthorId)
                                    .Select(a => a.FullName)
                                    .FirstOrDefault()
                                : "Unknown"
                            orderby b.CreatedAt descending
                            select new BookCardViewModel
                            {
                                BookId = b.BookId,
                                Title = b.Title,
                                AuthorName = author,
                                CoverUrl = cover,
                                AverageRating = b.AverageRating ?? 0,
                                CreatedAt = b.CreatedAt
                            })
                            .Take(8)
                            .ToList();

            var topFeaturedBooks = (from b in db.Books
                                    where b.IsActive
                                    let realAvgRating = (decimal?)db.Reviews
                                        .Where(r => r.BookId == b.BookId)
                                        .Average(r => (double?)r.Rating)
                                    where realAvgRating != null
                                    let cover = db.BookImages
                                        .Where(i => i.BookId == b.BookId && i.IsPrimary)
                                        .Select(i => i.ImageUrl)
                                        .FirstOrDefault()
                                    let author = b.AuthorId != null
                                        ? db.Authors
                                            .Where(a => a.AuthorId == b.AuthorId)
                                            .Select(a => a.FullName)
                                            .FirstOrDefault()
                                        : "Unknown"
                                    orderby realAvgRating descending
                                    select new BookCardViewModel
                                    {
                                        BookId = b.BookId,
                                        Title = b.Title,
                                        AuthorName = author,
                                        CoverUrl = cover,
                                        AverageRating = realAvgRating ?? 0,
                                        CreatedAt = b.CreatedAt
                                    })
                                    .Take(4)
                                    .ToList();

            var dynamicBooks = new List<BookCardViewModel>();
            string sectionTitle = "Featured Books";

            string currentRole = Session["Role"] != null ? Session["Role"].ToString() : "";
            int? currentUserId = Session["UserId"] as int?;

            if (currentRole == "Reader" && currentUserId.HasValue)
            {
                var currentReader = db.Readers.FirstOrDefault(r => r.UserId == currentUserId.Value);

                if (currentReader != null)
                {
                    var userBorrowedCategoryIds = (from br in db.Borrowings
                                                   join bd in db.BorrowingDetails on br.BorrowingId equals bd.BorrowingId
                                                   join b in db.Books on bd.BookId equals b.BookId
                                                   where br.ReaderId == currentReader.ReaderId
                                                   select b.CategoryId)
                                                   .Distinct()
                                                   .ToList();

                    if (userBorrowedCategoryIds.Any())
                    {
                        sectionTitle = "Recommended For You";

                        dynamicBooks = (from b in db.Books
                                        where b.IsActive && userBorrowedCategoryIds.Contains(b.CategoryId)
                                        let realAvgRating = (decimal?)db.Reviews
                                            .Where(r => r.BookId == b.BookId)
                                            .Average(r => (double?)r.Rating)
                                        let cover = db.BookImages
                                            .Where(i => i.BookId == b.BookId && i.IsPrimary)
                                            .Select(i => i.ImageUrl)
                                            .FirstOrDefault()
                                        let author = b.AuthorId != null
                                            ? db.Authors
                                                .Where(a => a.AuthorId == b.AuthorId)
                                                .Select(a => a.FullName)
                                                .FirstOrDefault()
                                            : "Unknown"
                                        orderby b.AverageRating descending
                                        select new BookCardViewModel
                                        {
                                            BookId = b.BookId,
                                            Title = b.Title,
                                            AuthorName = author,
                                            CoverUrl = cover,
                                            AverageRating = realAvgRating ?? 0,
                                            CreatedAt = b.CreatedAt
                                        })
                                        .Take(4)
                                        .ToList();

                        if (dynamicBooks.Count < 4)
                        {
                            int itemsNeeded = 4 - dynamicBooks.Count;

                            var fallbackFill = topFeaturedBooks
                                .Where(fb => !dynamicBooks.Any(dbk => dbk.BookId == fb.BookId))
                                .Take(itemsNeeded)
                                .ToList();

                            dynamicBooks.AddRange(fallbackFill);
                        }
                    }
                }
            }

            if (!dynamicBooks.Any())
            {
                sectionTitle = "Featured Books";
                dynamicBooks = topFeaturedBooks;
            }

            int selCat = categoryId ?? 0;

            var catQuery = db.Books.Where(b => b.IsActive);

            if (selCat > 0)
            {
                catQuery = catQuery.Where(b => b.CategoryId == selCat);
            }

            var categoryBooks = (from b in catQuery
                                 let cover = db.BookImages
                                    .Where(i => i.BookId == b.BookId && i.IsPrimary)
                                    .Select(i => i.ImageUrl)
                                    .FirstOrDefault()
                                 let author = b.AuthorId != null
                                    ? db.Authors
                                        .Where(a => a.AuthorId == b.AuthorId)
                                        .Select(a => a.FullName)
                                        .FirstOrDefault()
                                    : "Unknown"
                                 orderby b.Title
                                 select new BookCardViewModel
                                 {
                                     BookId = b.BookId,
                                     Title = b.Title,
                                     AuthorName = author,
                                     CoverUrl = cover,
                                     AverageRating = b.AverageRating ?? 0,
                                     CreatedAt = b.CreatedAt
                                 })
                                 .Take(8)
                                 .ToList();

            BookDetailViewModel selected = null;

            if (selectedBookId.HasValue)
            {
                selected = GetBookDetail(selectedBookId.Value);
            }

            var vm = new HomeViewModel
            {
                NewBooks = newBooks,
                DynamicBooks = dynamicBooks,
                DynamicSectionTitle = sectionTitle,
                CategoryBooks = categoryBooks,
                Categories = categories,
                SelectedCategoryId = selCat,
                SelectedBook = selected
            };

            return View(vm);
        }

        // ─────────────────────────────────────────
        // BOOK LIST
        // ─────────────────────────────────────────
        public ActionResult BookList(int? categoryId, string q, string sort, string letter, string view, int? selectedBookId, int page = 1)
        {
            var categories = db.Categories
                .Where(c => c.IsActive)
                .Select(c => new CategoryItem
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName
                })
                .ToList();

            var query = db.Books.Where(b => b.IsActive);

            string categoryName = "All Books";

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                query = query.Where(b => b.CategoryId == categoryId.Value);

                var cat = db.Categories.FirstOrDefault(c => c.CategoryId == categoryId.Value);

                if (cat != null)
                {
                    categoryName = cat.CategoryName;
                }
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                string ql = q.ToLower();

                query = query.Where(b =>
                    b.Title.ToLower().Contains(ql) ||
                    (b.ISBN != null && b.ISBN.ToLower().Contains(ql)) ||
                    (b.Author != null && b.Author.FullName.ToLower().Contains(ql)) ||
                    (b.Category != null && b.Category.CategoryName.ToLower().Contains(ql))
                );
            }

            if (!string.IsNullOrEmpty(letter) && letter != "All")
            {
                string l = letter.ToUpper();

                query = query.Where(b =>
                    b.Title.ToUpper().StartsWith(l) ||
                    (b.Title.ToUpper().StartsWith("THE ") && b.Title.ToUpper().Substring(4).StartsWith(l))
                );
            }

            switch (sort)
            {
                case "author":
                    query = query.OrderBy(b => b.Author != null ? b.Author.FullName : "");
                    break;

                case "newest":
                    query = query.OrderByDescending(b => b.CreatedAt);
                    break;

                case "rating":
                    query = query.OrderByDescending(b => b.AverageRating);
                    break;

                case "published":
                    query = query.OrderByDescending(b => b.PublishYear);
                    break;

                default:
                    query = query.OrderBy(b => b.Title);
                    break;
            }

            int pageSize = 8;

            if (page < 1)
            {
                page = 1;
            }

            int totalItems = query.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            if (totalPages < 1)
            {
                totalPages = 1;
            }

            if (page > totalPages)
            {
                page = totalPages;
            }

            var books = (from b in query
                         let cover = db.BookImages
                            .Where(i => i.BookId == b.BookId && i.IsPrimary)
                            .Select(i => i.ImageUrl)
                            .FirstOrDefault()
                         let author = b.AuthorId != null
                            ? db.Authors
                                .Where(a => a.AuthorId == b.AuthorId)
                                .Select(a => a.FullName)
                                .FirstOrDefault()
                            : "Unknown"
                         let category = b.CategoryId != null
                            ? db.Categories
                                .Where(c => c.CategoryId == b.CategoryId)
                                .Select(c => c.CategoryName)
                                .FirstOrDefault()
                            : "General"
                         let publisher = b.PublisherId != null
                            ? db.Publishers
                                .Where(p => p.PublisherId == b.PublisherId)
                                .Select(p => p.PublisherName)
                                .FirstOrDefault()
                            : "Unknown Publisher"
                         select new BookCardViewModel
                         {
                             BookId = b.BookId,
                             Title = b.Title,
                             AuthorName = author,
                             CoverUrl = cover,
                             AverageRating = b.AverageRating ?? 0,
                             CreatedAt = b.CreatedAt,
                             CategoryName = category,
                             PublisherName = publisher
                         })
                         .Skip((page - 1) * pageSize)
                         .Take(pageSize)
                         .ToList();

            BookDetailViewModel selected = null;

            if (selectedBookId.HasValue)
            {
                selected = GetBookDetail(selectedBookId.Value);
            }

            var vm = new BookListViewModel
            {
                Books = books,
                Categories = categories,
                SearchQuery = q,
                SortBy = sort ?? "title",
                FilterLetter = letter ?? "All",
                ViewMode = view ?? "grid",
                CategoryId = categoryId,
                CategoryName = categoryName,
                SelectedBook = selected,
                CurrentPage = page,
                TotalPages = totalPages,
                TotalItems = totalItems
            };

            return View(vm);
        }

        // ─────────────────────────────────────────
        // AJAX: BOOK DETAIL PANEL
        // ─────────────────────────────────────────
        [HttpGet]
        public ActionResult BookDetail(int id)
        {
            var detail = GetBookDetail(id);

            if (detail == null)
            {
                return HttpNotFound();
            }

            return PartialView("_BookDetail", detail);
        }

        // ─────────────────────────────────────────
        // CATEGORY MENU
        // ─────────────────────────────────────────
        [HttpGet]
        public ActionResult CategoryMenu()
        {
            var cats = db.Categories
                .Where(c => c.IsActive)
                .Select(c => new CategoryItem
                {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName
                })
                .ToList();

            return PartialView("~/Views/Guest/_CategoryMenu.cshtml", cats);
        }

        // ─────────────────────────────────────────
        // SEARCH
        // ─────────────────────────────────────────
        public ActionResult Search(string q)
        {
            return RedirectToAction("BookList", new { q = q });
        }

        // ─────────────────────────────────────────
        // SETTING
        // ─────────────────────────────────────────
        public ActionResult Setting()
        {
            return View();
        }

        // ─────────────────────────────────────────
        // SUPPORT
        // ─────────────────────────────────────────
        public ActionResult Support()
        {
            return View();
        }

        // ─────────────────────────────────────────
        // HELPER: BOOK DETAIL
        // ─────────────────────────────────────────
        private BookDetailViewModel GetBookDetail(int id)
        {
            var b = db.Books.FirstOrDefault(x => x.BookId == id && x.IsActive);

            if (b == null)
            {
                return null;
            }

            var cover = db.BookImages
                .Where(i => i.BookId == id && i.IsPrimary)
                .Select(i => i.ImageUrl)
                .FirstOrDefault();

            var authorName = b.AuthorId != null
                ? db.Authors
                    .Where(a => a.AuthorId == b.AuthorId)
                    .Select(a => a.FullName)
                    .FirstOrDefault()
                : "Unknown";

            var catName = b.CategoryId != null
                ? db.Categories
                    .Where(c => c.CategoryId == b.CategoryId)
                    .Select(c => c.CategoryName)
                    .FirstOrDefault()
                : "";

            var pubName = b.PublisherId != null
                ? db.Publishers
                    .Where(p => p.PublisherId == b.PublisherId)
                    .Select(p => p.PublisherName)
                    .FirstOrDefault()
                : "";

            return new BookDetailViewModel
            {
                BookId = b.BookId,
                Title = b.Title,
                AuthorName = authorName,
                CoverUrl = cover,
                AverageRating = b.AverageRating ?? 0,
                TotalReviews = b.TotalReviews ?? 0,
                Description = b.Description,
                AvailableCopies = b.AvailableCopies,
                ISBN = b.ISBN,
                PublishYear = b.PublishYear,
                CategoryName = catName,
                Language = b.Language,
                PageCount = b.PageCount,
                PublisherName = pubName,

                PreviewContent = b.PreviewContent,
                EbookUrl = b.EbookUrl
            };
        }

        [HttpGet]
        public ActionResult GetBookComments(int id)
        {
            var comments = (from r in db.Reviews
                            join rd in db.Readers on r.ReaderId equals rd.ReaderId into jr
                            from rd in jr.DefaultIfEmpty()
                            join u in db.UserAccounts on rd.UserId equals u.UserId into ju
                            from u in ju.DefaultIfEmpty()
                            where r.BookId == id && r.IsVisible
                            orderby r.ReviewDate descending
                            select new
                            {
                                UserName = u != null ? u.FullName : "Anonymous",
                                Rating = r.Rating,
                                Text = r.Comment
                            })
                            .ToList();

            return Json(comments, JsonRequestBehavior.AllowGet);
        }
        public ActionResult ReadEbook(int id)
        {
            var book = db.Books.FirstOrDefault(b => b.BookId == id);
            if (book == null || string.IsNullOrEmpty(book.EbookUrl))
                return HttpNotFound();

            ViewBag.EbookUrl = book.EbookUrl;
            return View("~/Views/Shared/EbookReader.cshtml");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}