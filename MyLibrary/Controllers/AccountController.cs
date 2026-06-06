using MyLibrary.Models;
using MyLibrary.Models.ViewModels;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;

namespace MyLibrary.Controllers
{
    public class AccountController : Controller
    {
        private LibraryDataContext db = new LibraryDataContext();

        // ── CHECK REMEMBER ME COOKIE ──
        private bool IsPersistentAuthCookie()
        {
            var cookie = Request.Cookies[FormsAuthentication.FormsCookieName];

            if (cookie == null || string.IsNullOrEmpty(cookie.Value))
                return false;

            try
            {
                var ticket = FormsAuthentication.Decrypt(cookie.Value);
                return ticket != null && ticket.IsPersistent;
            }
            catch
            {
                return false;
            }
        }

        // ── CLEAR LOGIN COOKIE ──
        private void ClearLoginCookie()
        {
            FormsAuthentication.SignOut();

            var expiredCookie = new HttpCookie(FormsAuthentication.FormsCookieName)
            {
                Expires = DateTime.Now.AddDays(-1),
                HttpOnly = true
            };

            Response.Cookies.Add(expiredCookie);

            Session.Clear();
            Session.Abandon();
        }

        // ── REFRESH SESSION ──
        private bool RefreshSession()
        {
            if (!Request.IsAuthenticated)
                return false;

            // Nếu session mất sau khi chạy lại project
            // mà cookie không phải Remember me thì bắt login lại
            if (Session["UserId"] == null && !IsPersistentAuthCookie())
            {
                ClearLoginCookie();
                return false;
            }

            int userId;

            if (!int.TryParse(User.Identity.Name, out userId))
            {
                ClearLoginCookie();
                return false;
            }

            var user = db.UserAccounts.FirstOrDefault(u => u.UserId == userId && u.IsActive);

            if (user == null)
            {
                ClearLoginCookie();
                return false;
            }

            Session["UserId"] = user.UserId;
            Session["FullName"] = user.FullName;
            Session["AvatarUrl"] = user.AvatarUrl ?? "";
            Session["Role"] = user.Role;

            return true;
        }

        // ── LOGIN GET ──
        public ActionResult Login(string returnUrl)
        {
            if (Request.IsAuthenticated)
            {
                if (!RefreshSession())
                {
                    ViewBag.ReturnUrl = returnUrl;
                    return View();
                }

                string role = Session["Role"] != null ? Session["Role"].ToString() : "";

                if (role == "Admin")
                    return RedirectToAction("Index", "Admin");

                if (role == "Librarian")
                    return RedirectToAction("Index", "Librarian");

                return RedirectToAction("Index", "Guest");
            }

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // ── LOGIN POST ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(LoginViewModel model, string returnUrl)
        {
            if (!ModelState.IsValid)
                return View(model);

            string hash = HashPassword(model.Password);

            var user = db.UserAccounts.FirstOrDefault(u =>
                u.Email == model.Email &&
                u.PasswordHash == hash &&
                u.IsActive);

            if (user == null)
            {
                ModelState.AddModelError("", "Invalid email or password.");
                return View(model);
            }

            if (user.Role != "Reader" && user.Role != "Librarian" && user.Role != "Admin")
            {
                ModelState.AddModelError("", "Access denied.");
                return View(model);
            }

            // Xóa cookie cũ trước, tránh bị dính session reader/admin cũ
            FormsAuthentication.SignOut();

            DateTime now = DateTime.Now;
            DateTime expireTime = model.RememberMe
                ? now.AddDays(30)
                : now.AddMinutes(60);

            var ticket = new FormsAuthenticationTicket(
                1,
                user.UserId.ToString(),
                now,
                expireTime,
                model.RememberMe,
                user.Role,
                FormsAuthentication.FormsCookiePath
            );

            string encryptedTicket = FormsAuthentication.Encrypt(ticket);

            var authCookie = new HttpCookie(FormsAuthentication.FormsCookieName, encryptedTicket)
            {
                HttpOnly = true
            };

            // Chỉ khi tick Remember me mới lưu cookie lâu dài
            if (model.RememberMe)
                authCookie.Expires = expireTime;

            Response.Cookies.Add(authCookie);

            Session["UserId"] = user.UserId;
            Session["FullName"] = user.FullName;
            Session["AvatarUrl"] = user.AvatarUrl ?? "";
            Session["Role"] = user.Role;

            // Không dùng returnUrl nữa để tránh Admin bị chuyển nhầm sang Librarian
            if (user.Role == "Admin")
                return RedirectToAction("Index", "Admin");

            if (user.Role == "Librarian")
                return RedirectToAction("Index", "Librarian");

            return RedirectToAction("Index", "Guest");
        }

        // ── REGISTER GET ──
        public ActionResult Register()
        {
            if (User.Identity.IsAuthenticated)
                return RedirectToAction("Index", "Guest");

            return View();
        }

        // ── REGISTER POST ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            if (db.UserAccounts.Any(u => u.Email == model.Email))
            {
                ModelState.AddModelError("Email", "This email is already registered.");
                return View(model);
            }

            var user = new UserAccount
            {
                Email = model.Email,
                PasswordHash = HashPassword(model.Password),
                FullName = model.FullName,
                Phone = model.Phone,
                Role = "Reader",
                IsActive = true,
                IsEmailVerified = false,
                FailedLoginAttempts = 0,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            db.UserAccounts.InsertOnSubmit(user);
            db.SubmitChanges();

            string readerCode = "RDR-" + user.UserId.ToString("D3");

            var reader = new Reader
            {
                UserId = user.UserId,
                ReaderCode = readerCode,
                MembershipDate = DateTime.Now.Date,
                MembershipExpiry = DateTime.Now.AddYears(1).Date,
                TotalBorrowed = 0,
                TotalFines = 0
            };

            db.Readers.InsertOnSubmit(reader);
            db.SubmitChanges();

            TempData["Success"] = "Account created! Please log in.";
            return RedirectToAction("Login");
        }

        // ── LOGOUT ──
        public ActionResult Logout()
        {
            ClearLoginCookie();
            return RedirectToAction("Index", "Guest");
        }

        // ── PROFILE GET ──
        [Authorize]
        public ActionResult Profile()
        {
            int userId = int.Parse(User.Identity.Name);

            using (var freshDb = new LibraryDataContext())
            {
                var user = freshDb.UserAccounts.FirstOrDefault(u => u.UserId == userId);

                if (user == null)
                    return RedirectToAction("Logout");

                var reader = freshDb.Readers.FirstOrDefault(r => r.UserId == userId);
                var librarian = freshDb.Librarians.FirstOrDefault(l => l.UserId == userId);

                var vm = new ProfileViewModel
                {
                    UserId = user.UserId,
                    Email = user.Email,
                    FullName = user.FullName,
                    Phone = user.Phone,
                    Address = user.Address,
                    AvatarUrl = user.AvatarUrl,
                    Role = user.Role,
                    IsActive = user.IsActive,

                    Gender = reader != null ? reader.Gender : null,
                    DateOfBirth = reader != null ? reader.DateOfBirth : null,
                    ReaderCode = reader != null ? reader.ReaderCode : "—",
                    MembershipDate = reader != null ? reader.MembershipDate : (DateTime?)null,
                    MembershipExpiry = reader != null ? reader.MembershipExpiry : (DateTime?)null,
                    TotalBorrowed = reader != null ? reader.TotalBorrowed : 0,
                    TotalFines = reader != null ? reader.TotalFines : 0,

                    LibrarianCode = librarian != null ? librarian.LibrarianCode : null,
                    Department = librarian != null ? librarian.Department : null,
                    HireDate = librarian != null ? librarian.HireDate : (DateTime?)null
                };

                return View(vm);
            }
        }

        // ── PROFILE POST ──
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Profile(ProfileViewModel model)
        {
            int userId = int.Parse(User.Identity.Name);

            var user = db.UserAccounts.FirstOrDefault(u => u.UserId == userId);

            if (user == null)
                return RedirectToAction("Logout");

            if (!user.IsActive)
            {
                TempData["Error"] = "Your account is deactivated.";
                return RedirectToAction("Profile");
            }

            user.FullName = model.FullName ?? user.FullName;
            user.Phone = model.Phone;
            user.Address = model.Address;
            user.UpdatedAt = DateTime.Now;

            if (!string.IsNullOrEmpty(model.AvatarUrl))
                user.AvatarUrl = model.AvatarUrl;

            db.SubmitChanges();

            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);

            if (reader != null)
            {
                reader.Gender = model.Gender;
                reader.DateOfBirth = model.DateOfBirth;
                db.SubmitChanges();
            }

            Session["FullName"] = user.FullName;
            Session["AvatarUrl"] = user.AvatarUrl ?? "";
            Session["Role"] = user.Role;

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("Profile");
        }

        // ── HASH PASSWORD ──
        private string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
                return BitConverter.ToString(bytes).Replace("-", "").ToUpper();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();

            base.Dispose(disposing);
        }
    }
}