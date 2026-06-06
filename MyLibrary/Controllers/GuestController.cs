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
                .Select(c => new CategoryItem { CategoryId = c.CategoryId, CategoryName = c.CategoryName })
                .ToList();

            // New books: 8 most recently added active books
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

            // Category books
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

            var vm = new HomeViewModel
            {
                NewBooks = newBooks,
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
        public ActionResult BookList(int? categoryId, string q, string sort, string letter, string view, int? selectedBookId)
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
                         }).ToList();

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
                SelectedBook = selected
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}