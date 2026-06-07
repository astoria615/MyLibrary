using System.Collections.Generic;

namespace MyLibrary.Models.ViewModels
{
    public class BookListViewModel
    {
        public List<BookCardViewModel> Books { get; set; }
        public List<CategoryItem> Categories { get; set; }
        public string SearchQuery { get; set; }
        public string SortBy { get; set; }       // "title","author","newest","rating"
        public string FilterLetter { get; set; } // A-Z or ""
        public string ViewMode { get; set; }     // "grid" or "list"
        public int? CategoryId { get; set; }
        public string CategoryName { get; set; }
        public BookDetailViewModel SelectedBook { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
        public int TotalItems { get; set; }
    }
}