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

            // 1. Existing Categories fetching
            var categories = db.Categories
                .Where(c => c.IsActive)
                .Select(c => new CategoryItem { CategoryId = c.CategoryId, CategoryName = c.CategoryName })
                .ToList();

            // 2. Existing New books fetching
            var newBooks = (from b in db.Books
                            where b.IsActive
                            let cover = db.BookImages.Where(i => i.BookId == b.BookId && i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                            let author = b.AuthorId != null ? db.Authors.Where(a => a.AuthorId == b.AuthorId).Select(a => a.FullName).FirstOrDefault() : "Unknown"
                            orderby b.CreatedAt descending
                            select new BookCardViewModel
                            {
                                BookId = b.BookId,
                                Title = b.Title,
                                AuthorName = author,
                                CoverUrl = cover,
                                AverageRating = b.AverageRating ?? 0,
                                CreatedAt = b.CreatedAt
                            }).Take(8).ToList();

            // 3. DYNAMIC LOGIC: Featured vs Recommended Books
            var dynamicBooks = new List<BookCardViewModel>();
            string sectionTitle = "Featured Books";

            string currentRole = Session["Role"]?.ToString();
            int? currentUserId = Session["UserId"] as int?;

            if (currentRole == "Reader" && currentUserId.HasValue)
            {
                // Traverse from Borrowings -> BorrowingDetails -> Books to track down the CategoryId
                var lastBorrowedCategoryId = (from br in db.Borrowings
                                              join bd in db.BorrowingDetails on br.BorrowingId equals bd.BorrowingId
                                              join b in db.Books on bd.BookId equals b.BookId
                                              where br.ReaderId == currentUserId.Value
                                              orderby br.BorrowDate descending
                                              select b.CategoryId).FirstOrDefault();

                if (lastBorrowedCategoryId > 0)
                {
                    sectionTitle = "Recommended For You";

                    // Query active books belonging to that exact category
                    dynamicBooks = (from b in db.Books
                                    where b.IsActive && b.CategoryId == lastBorrowedCategoryId
                                    let cover = db.BookImages.Where(i => i.BookId == b.BookId && i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                    let author = b.AuthorId != null ? db.Authors.Where(a => a.AuthorId == b.AuthorId).Select(a => a.FullName).FirstOrDefault() : "Unknown"
                                    orderby b.AverageRating descending
                                    select new BookCardViewModel
                                    {
                                        BookId = b.BookId,
                                        Title = b.Title,
                                        AuthorName = author,
                                        CoverUrl = cover,
                                        AverageRating = b.AverageRating ?? 0,
                                        CreatedAt = b.CreatedAt
                                    }).Take(4).ToList();
                }
            }

            // Fallback: If role is Guest OR reader has never borrowed anything yet, show standard Featured Books
            if (!dynamicBooks.Any())
            {
                sectionTitle = "Featured Books";
                dynamicBooks = (from b in db.Books
                                where b.IsActive && b.IsFeatured == true
                                let cover = db.BookImages.Where(i => i.BookId == b.BookId && i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                let author = b.AuthorId != null ? db.Authors.Where(a => a.AuthorId == b.AuthorId).Select(a => a.FullName).FirstOrDefault() : "Unknown"
                                orderby b.Title
                                select new BookCardViewModel
                                {
                                    BookId = b.BookId,
                                    Title = b.Title,
                                    AuthorName = author,
                                    CoverUrl = cover,
                                    AverageRating = b.AverageRating ?? 0,
                                    CreatedAt = b.CreatedAt
                                }).Take(4).ToList();
            }

            // 4. Existing Category books filtering logic
            int selCat = categoryId ?? 0;
            var catQuery = db.Books.Where(b => b.IsActive);
            if (selCat > 0) catQuery = catQuery.Where(b => b.CategoryId == selCat);

            var categoryBooks = (from b in catQuery
                                 let cover = db.BookImages.Where(i => i.BookId == b.BookId && i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                                 let author = b.AuthorId != null ? db.Authors.Where(a => a.AuthorId == b.AuthorId).Select(a => a.FullName).FirstOrDefault() : "Unknown"
                                 orderby b.Title
                                 select new BookCardViewModel
                                 {
                                     BookId = b.BookId,
                                     Title = b.Title,
                                     AuthorName = author,
                                     CoverUrl = cover,
                                     AverageRating = b.AverageRating ?? 0,
                                     CreatedAt = b.CreatedAt
                                 }).Take(8).ToList();

            BookDetailViewModel selected = null;
            if (selectedBookId.HasValue)
                selected = GetBookDetail(selectedBookId.Value);

            // 5. Send everything right to the ViewModel wrapper
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
        // BOOK LIST (All / by Category)
        // ─────────────────────────────────────────
        public ActionResult BookList(int? categoryId, string q, string sort, string letter, string view, int? selectedBookId, int page = 1)
        {
            var categories = db.Categories
                .Where(c => c.IsActive)
                .Select(c => new CategoryItem { CategoryId = c.CategoryId, CategoryName = c.CategoryName })
                .ToList();

            var query = db.Books.Where(b => b.IsActive);

            // Filter by category
            string categoryName = "All Books";
            if (categoryId.HasValue && categoryId.Value > 0)
            {
                query = query.Where(b => b.CategoryId == categoryId.Value);
                var cat = db.Categories.FirstOrDefault(c => c.CategoryId == categoryId.Value);
                if (cat != null) categoryName = cat.CategoryName;
            }

            // Search
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

            // Letter filter
            if (!string.IsNullOrEmpty(letter) && letter != "All")
            {
                string l = letter.ToUpper();
                query = query.Where(b =>
                    b.Title.ToUpper().StartsWith(l) ||
                    (b.Title.ToUpper().StartsWith("THE ") && b.Title.ToUpper().Substring(4).StartsWith(l))
                );
            }

            // Sort
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
                default: // title
                    query = query.OrderBy(b => b.Title);
                    break;
            }

            // ─────────────────────────────────────────
            // PAGINATION LOGIC (8 books per page)
            // ─────────────────────────────────────────
            int pageSize = 8;
            if (page < 1) page = 1;

            // Get the total items match BEFORE skipping
            int totalItems = query.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            // Projection with Pagination Offsets
            var books = (from b in query
                         let cover = db.BookImages.Where(i => i.BookId == b.BookId && i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault()
                         let author = b.AuthorId != null ? db.Authors.Where(a => a.AuthorId == b.AuthorId).Select(a => a.FullName).FirstOrDefault() : "Unknown"
                         select new BookCardViewModel
                         {
                             BookId = b.BookId,
                             Title = b.Title,
                             AuthorName = author,
                             CoverUrl = cover,
                             AverageRating = b.AverageRating ?? 0,
                             CreatedAt = b.CreatedAt
                         })
                         .Skip((page - 1) * pageSize)
                         .Take(pageSize)
                         .ToList();

            BookDetailViewModel selected = null;
            if (selectedBookId.HasValue)
                selected = GetBookDetail(selectedBookId.Value);

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

                // Pass values to View
                CurrentPage = page,
                TotalPages = totalPages,
                TotalItems = totalItems
            };

            return View(vm);
        }
        // ─────────────────────────────────────────
        // AJAX: Get book detail panel
        // ─────────────────────────────────────────
        [HttpGet]
        public ActionResult BookDetail(int id)
        {
            var detail = GetBookDetail(id);
            if (detail == null) return HttpNotFound();
            return PartialView("_BookDetail", detail);
        }

        // ─────────────────────────────────────────
        // CATEGORY nav dropdown (AJAX)
        // ─────────────────────────────────────────
        // ── CATEGORY nav dropdown (AJAX) ──
        [HttpGet]
        public ActionResult CategoryMenu()
        {
            var cats = db.Categories
                .Where(c => c.IsActive)
                .Select(c => new CategoryItem { CategoryId = c.CategoryId, CategoryName = c.CategoryName })
                .ToList();

            // 💡 Specifying the explicit path bypasses MVC's folder search logic entirely
            return PartialView("~/Views/Guest/_CategoryMenu.cshtml", cats);
        }

        // ─────────────────────────────────────────
        // SEARCH (redirects to BookList)
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
        // HELPER
        // ─────────────────────────────────────────
        private BookDetailViewModel GetBookDetail(int id)
        {
            var b = db.Books.FirstOrDefault(x => x.BookId == id && x.IsActive);
            if (b == null) return null;

            var cover = db.BookImages.Where(i => i.BookId == id && i.IsPrimary).Select(i => i.ImageUrl).FirstOrDefault();
            var authorName = b.AuthorId != null ? db.Authors.Where(a => a.AuthorId == b.AuthorId).Select(a => a.FullName).FirstOrDefault() : "Unknown";
            var catName = b.CategoryId != null ? db.Categories.Where(c => c.CategoryId == b.CategoryId).Select(c => c.CategoryName).FirstOrDefault() : "";
            var pubName = b.PublisherId != null ? db.Publishers.Where(p => p.PublisherId == b.PublisherId).Select(p => p.PublisherName).FirstOrDefault() : "";

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
                PublisherName = pubName
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
                            }).ToList();

            return Json(comments, JsonRequestBehavior.AllowGet);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}