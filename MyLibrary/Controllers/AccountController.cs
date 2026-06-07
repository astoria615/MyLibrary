using MyLibrary.Models;
using MyLibrary.Models.ViewModels;
using System;
using System.Linq;
using System.Net;
using System.Net.Mail;
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

            if (model.RememberMe)
                authCookie.Expires = expireTime;

            Response.Cookies.Add(authCookie);

            Session["UserId"] = user.UserId;
            Session["FullName"] = user.FullName;
            Session["AvatarUrl"] = user.AvatarUrl ?? "";
            Session["Role"] = user.Role;

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

        // ── REGISTER POST (UPDATED WITH 4-DIGIT CODE VALIDATION) ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(RegisterViewModel model, string inputCode)
        {
            if (!ModelState.IsValid)
                return View(model);

            // 1. Fetch saved validation info from Session state cache
            string sessionCode = Session["EmailVerificationCode"] as string;
            string sessionEmail = Session["TargetVerificationEmail"] as string;

            // 2. Validate code match state limits
            if (string.IsNullOrEmpty(sessionCode) || inputCode != sessionCode || model.Email != sessionEmail)
            {
                ModelState.AddModelError("", "The verification code is incorrect, mismatched, or has expired.");
                return View(model);
            }

            // 3. Double check database availability path right before database writing operations
            if (db.UserAccounts.Any(u => u.Email == model.Email))
            {
                ModelState.AddModelError("Email", "This email address was registered by another user session.");
                return View(model);
            }

            // 4. Verification Successful! Commit account record elements
            var user = new UserAccount
            {
                Email = model.Email,
                PasswordHash = HashPassword(model.Password),
                FullName = model.FullName,
                Phone = model.Phone,
                Role = "Reader",
                IsActive = true,
                IsEmailVerified = true, // Flagged true since verified via email channel
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

            // 5. Clean up temporary session usage values
            Session["EmailVerificationCode"] = null;
            Session["TargetVerificationEmail"] = null;

            TempData["Success"] = "Account verified and created successfully! Please log in.";
            return RedirectToAction("Login");
        }

        // ── AJAX SERVICE: GENERATE AND DISTRIBUTE CODES OVER SMTP MAIL NETWORKS ──
        [HttpPost]
        public JsonResult SendVerificationCode(string email, string fullName)
        {
            try
            {
                // Safety checkpoint: Block sending if the email already belongs to a registered account
                if (db.UserAccounts.Any(u => u.Email == email))
                {
                    return Json(new { success = false, message = "This email is already registered to another account context." });
                }

                // Generate a random 4-digit security code
                Random rand = new Random();
                string verificationCode = rand.Next(1000, 9999).ToString();

                // Store code information context into active Session
                Session["EmailVerificationCode"] = verificationCode;
                Session["TargetVerificationEmail"] = email;

                // ⚠️ SYSTEM ACCOUNT SMTP PARAMETERS
                string mySenderEmail = "diepchi793@gmail.com";
                string myAppPassword = "zwjjvknonjhuwnep"; // Insert 16-character password here with no spaces

                using (MailMessage mail = new MailMessage())
                {
                    mail.From = new MailAddress(mySenderEmail, "My Library Security");
                    mail.To.Add(email);
                    mail.Subject = "Your My Library Verification Code";
                    mail.Body = $@"Hello {fullName},

Your 4-digit account security verification code is: {verificationCode}

Please enter this code on the registration page interface component to verify your registration request.

Best regards,
My Library System Administration";

                    mail.IsBodyHtml = false;

                    using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587))
                    {
                        smtp.UseDefaultCredentials = false;
                        smtp.Credentials = new NetworkCredential(mySenderEmail, myAppPassword);
                        smtp.EnableSsl = true;
                        smtp.DeliveryMethod = SmtpDeliveryMethod.Network;

                        smtp.Send(mail);
                    }
                }

                return Json(new { success = true, message = "Verification text dispatched seamlessly to your real email listing address!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "SMTP pipeline handling error: " + ex.GetBaseException().Message });
            }
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
        [HttpGet] // Ensure this is a GET request
        public ActionResult Logout()
        {
            // 1. Clear session and cookie
            FormsAuthentication.SignOut();
            Session.Clear();
            Session.Abandon();

            // 2. Explicitly clear the auth cookie
            var cookie = new HttpCookie(FormsAuthentication.FormsCookieName)
            {
                Expires = DateTime.Now.AddDays(-1)
            };
            Response.Cookies.Add(cookie);

            // 3. Force redirect to Guest Index
            return RedirectToAction("Index", "Guest");
        }
        // ── STAGE 1: GET - Show Forgot Password Page ──
        public ActionResult ForgotPassword()
        {
            return View();
        }

        // ── STAGE 2: POST - Send Verification Code ──
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ForgotPassword(string email)
        {
            var user = db.UserAccounts.FirstOrDefault(u => u.Email == email);
            if (user == null)
            {
                ModelState.AddModelError("", "Email not found.");
                return View();
            }

            string code = new Random().Next(1000, 9999).ToString();
            Session["ResetCode"] = code;
            Session["ResetEmail"] = email;

            // Reuse your existing SMTP logic here
            SendEmail(email, "Password Reset", $"Your 4-digit reset code is: {code}");

            return RedirectToAction("VerifyResetCode");
        }

        // ── STAGE 3: GET/POST - Verify Code ──
        public ActionResult VerifyResetCode() { return View(); }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult VerifyResetCode(string inputCode)
        {
            if (Session["ResetCode"]?.ToString() == inputCode)
                return RedirectToAction("ResetPassword");

            ModelState.AddModelError("", "Invalid code.");
            return View();
        }

        // ── STAGE 4: POST - Finalize Password Reset ──
        public ActionResult ResetPassword() { return View(); }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ResetPassword(string newPassword)
        {
            string email = Session["ResetEmail"]?.ToString();
            var user = db.UserAccounts.FirstOrDefault(u => u.Email == email);

            if (user != null)
            {
                user.PasswordHash = HashPassword(newPassword);
                db.SubmitChanges();

                // Clear session
                Session["ResetCode"] = null;
                Session["ResetEmail"] = null;

                TempData["Success"] = "Password reset successful!";
                return RedirectToAction("Login");
            }
            return View();
        }
        // Add this private helper method to your AccountController
        private void SendEmail(string to, string subject, string body)
        {
            // ⚠️ REPLACE THESE WITH YOUR REAL CREDENTIALS
            string mySenderEmail = "diepchi793@gmail.com";
            string myAppPassword = "zwjjvknonjhuwnep";

            using (MailMessage mail = new MailMessage())
            {
                mail.From = new MailAddress(mySenderEmail, "My Library Security");
                mail.To.Add(to);
                mail.Subject = subject;
                mail.Body = body;
                mail.IsBodyHtml = false;

                using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587))
                {
                    smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential(mySenderEmail, myAppPassword);
                    smtp.EnableSsl = true;
                    smtp.Send(mail);
                }
            }
        }
        // ── PROFILE GET ──
        [Authorize]
        public ActionResult Profile()
        {
            int userId;
            if (!int.TryParse(User.Identity.Name, out userId))
                return RedirectToAction("Logout");

            using (var freshDb = new LibraryDataContext())
            {
                var user = freshDb.UserAccounts.FirstOrDefault(u => u.UserId == userId);
                if (user == null) return RedirectToAction("Logout");

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
            int userId;
            if (!int.TryParse(User.Identity.Name, out userId))
                return RedirectToAction("Logout");

            var user = db.UserAccounts.FirstOrDefault(u => u.UserId == userId);
            if (user == null) return RedirectToAction("Logout");

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

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("Profile");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                db.Dispose();

            base.Dispose(disposing);
        }
    }
}