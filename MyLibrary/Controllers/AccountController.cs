using MyLibrary.Models;
using MyLibrary.Models.ViewModels;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Mvc;
using System.Web.Security;


namespace MyLibrary.Controllers
{
    public class AccountController : Controller
    {
        private LibraryDataContext db = new LibraryDataContext();

        // ── LOGIN ──
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
        public ActionResult Login(string returnUrl)
        {
            if (Request.IsAuthenticated)
            {
                string role = Session["Role"] != null ? Session["Role"].ToString() : "";
                if (role == "Librarian" || role == "Admin")
                    return RedirectToAction("Index", "Librarian");
                return RedirectToAction("Index", "Guest");
            }
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(LoginViewModel model, string returnUrl)
        {
            if (!ModelState.IsValid) return View(model);

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

            // Only allow Reader and Librarian/Admin to log in here
            if (user.Role != "Reader" && user.Role != "Librarian" && user.Role != "Admin")
            {
                ModelState.AddModelError("", "Access denied.");
                return View(model);
            }

            // Set auth cookie — persistent ONLY if Remember Me checked
            FormsAuthentication.SetAuthCookie(user.UserId.ToString(), model.RememberMe);

            // Store session data
            Session["UserId"] = user.UserId;
            Session["FullName"] = user.FullName;
            Session["AvatarUrl"] = user.AvatarUrl ?? "";
            Session["Role"] = user.Role;

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            // Redirect based on role
            if (user.Role == "Librarian" || user.Role == "Admin")
                return RedirectToAction("Index", "Librarian");

            return RedirectToAction("Index", "Guest");
        }
        public ActionResult Register()
        {
            if (User.Identity.IsAuthenticated) return RedirectToAction("Index", "Guest");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

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

            // Create Reader profile
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
            FormsAuthentication.SignOut();
            Session.Clear();
            return RedirectToAction("Index", "Guest");
        }

        // ── PROFILE ──
        [Authorize]
        public ActionResult Profile()
        {
            int userId = int.Parse(User.Identity.Name);
            var user = db.UserAccounts.FirstOrDefault(u => u.UserId == userId);
            if (user == null) return RedirectToAction("Logout");

            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            var vm = new ProfileViewModel
            {
                UserId = user.UserId,
                Email = user.Email,
                FullName = user.FullName,
                Phone = user.Phone,
                Address = user.Address,
                AvatarUrl = user.AvatarUrl,
                Gender = reader?.Gender,
                DateOfBirth = reader?.DateOfBirth,
                ReaderCode = reader?.ReaderCode ?? "—",
                MembershipDate = reader?.MembershipDate,
                MembershipExpiry = reader?.MembershipExpiry,
                TotalBorrowed = reader?.TotalBorrowed ?? 0,
                TotalFines = reader?.TotalFines ?? 0
            };
            return View(vm);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Profile(ProfileViewModel model)
        {
            int userId = int.Parse(User.Identity.Name);
            var user = db.UserAccounts.FirstOrDefault(u => u.UserId == userId);
            if (user == null) return RedirectToAction("Logout");

            user.FullName = model.FullName;
            user.Phone = model.Phone;
            user.Address = model.Address;
            if (!string.IsNullOrEmpty(model.AvatarUrl))
                user.AvatarUrl = model.AvatarUrl;
            user.UpdatedAt = DateTime.Now;

            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            if (reader != null)
            {
                reader.Gender = model.Gender;
                reader.DateOfBirth = model.DateOfBirth;
            }

            db.SubmitChanges();

            Session["FullName"] = user.FullName;
            Session["AvatarUrl"] = user.AvatarUrl;

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("Profile");
        }

        // ── HELPER ──
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
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}