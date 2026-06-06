using System.Collections.Generic;

namespace MyLibrary.Models.ViewModels
{
    public class HomeViewModel
    {
        public List<BookCardViewModel> NewBooks { get; set; }
        public List<BookCardViewModel> CategoryBooks { get; set; }
        public List<CategoryItem> Categories { get; set; }
        public int SelectedCategoryId { get; set; }
        public BookDetailViewModel SelectedBook { get; set; }
    }

    public class BookCardViewModel
    {
        public int BookId { get; set; }
        public string Title { get; set; }
        public string AuthorName { get; set; }
        public string CoverUrl { get; set; }
        public decimal AverageRating { get; set; }
        public int PublishYear { get; set; }
        public System.DateTime CreatedAt { get; set; }
    }

    public class BookDetailViewModel
    {
        public int BookId { get; set; }
        public string Title { get; set; }
        public string AuthorName { get; set; }
        public string CoverUrl { get; set; }
        public decimal AverageRating { get; set; }
        public int TotalReviews { get; set; }
        public string Description { get; set; }
        public int AvailableCopies { get; set; }
        public string ISBN { get; set; }
        public int? PublishYear { get; set; }
        public string CategoryName { get; set; }
        public string Language { get; set; }
        public int? PageCount { get; set; }
        public string PublisherName { get; set; }
    }

    public class CategoryItem
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; }
    }
}