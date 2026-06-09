using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Net;
using System.Net.Mail;
using System.Threading;
using MyLibrary.Models;

namespace MyLibrary
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            Thread reminderThread = new Thread(StartReminderJob);
            reminderThread.IsBackground = true;
            reminderThread.Start();
        }
        private void StartReminderJob()
        {
            while (true)
            {
                try
                {
                    SendDueDateReminders();
                }
                catch { }

                // Check every 24 hours
                Thread.Sleep(TimeSpan.FromHours(24));
            }
        }

        private void SendDueDateReminders()
        {
            using (var db = new MyLibrary.Models.LibraryDataContext())
            {
                DateTime tomorrow = DateTime.Now.AddDays(1).Date;
                DateTime dayAfter = DateTime.Now.AddDays(2).Date;

                // Get borrowings due within 2 days
                var dueSoon = db.Borrowings
                    .Where(b => b.Status == "Approved" &&
                                b.DueDate >= tomorrow &&
                                b.DueDate < dayAfter)
                    .ToList();

                foreach (var borrow in dueSoon)
                {
                    // Check if reminder already sent today
                    var alreadySent = db.EmailNotifications
                        .Any(n => n.UserId == db.Readers
                            .Where(r => r.ReaderId == borrow.ReaderId)
                            .Select(r => r.UserId)
                            .FirstOrDefault() &&
                            n.NotificationType == "ReturnReminder" &&
                            n.CreatedAt >= DateTime.Today);

                    if (alreadySent) continue;

                    var reader = db.Readers.FirstOrDefault(r => r.ReaderId == borrow.ReaderId);
                    if (reader == null) continue;

                    var user = db.UserAccounts.FirstOrDefault(u => u.UserId == reader.UserId);
                    if (user == null || string.IsNullOrEmpty(user.Email)) continue;

                    var titles = (from d in db.BorrowingDetails
                                  join b in db.Books on d.BookId equals b.BookId
                                  where d.BorrowingId == borrow.BorrowingId
                                  select b.Title).ToList();

                    string bookList = "- " + string.Join("\n- ", titles);

                    string subject = "⏰ Return Reminder — Books Due Tomorrow";
                    string body = string.Format(
        @"Hello {0},

This is a reminder that your borrowed books are due tomorrow.

Borrowing ID: #{1}

Books:
{2}

Borrow Date: {3:dd/MM/yyyy}
Due Date: {4:dd/MM/yyyy}

Please return the books on time to avoid late fines.

Best regards,
My Library System",
                        user.FullName,
                        borrow.BorrowingId,
                        bookList,
                        borrow.BorrowDate,
                        borrow.DueDate
                    );

                    // Send email
                    try
                    {
                        string senderEmail = System.Configuration.ConfigurationManager.AppSettings["SmtpEmail"];
                        string appPassword = System.Configuration.ConfigurationManager.AppSettings["SmtpAppPassword"];

                        using (var mail = new System.Net.Mail.MailMessage())
                        {
                            mail.From = new System.Net.Mail.MailAddress(senderEmail, "My Library System");
                            mail.To.Add(user.Email);
                            mail.Subject = subject;
                            mail.Body = body;
                            mail.IsBodyHtml = false;

                            using (var smtp = new System.Net.Mail.SmtpClient("smtp.gmail.com", 587))
                            {
                                smtp.UseDefaultCredentials = false;
                                smtp.Credentials = new System.Net.NetworkCredential(senderEmail, appPassword);
                                smtp.EnableSsl = true;
                                smtp.Send(mail);
                            }
                        }

                        // Save notification
                        db.EmailNotifications.InsertOnSubmit(new MyLibrary.Models.EmailNotification
                        {
                            UserId = user.UserId,
                            NotificationType = "ReturnReminder",
                            Subject = subject,
                            Body = body,
                            Status = "Sent",
                            SentAt = DateTime.Now,
                            CreatedAt = DateTime.Now
                        });
                    }
                    catch (Exception ex)
                    {
                        db.EmailNotifications.InsertOnSubmit(new MyLibrary.Models.EmailNotification
                        {
                            UserId = user.UserId,
                            NotificationType = "ReturnReminder",
                            Subject = subject,
                            Body = body + "\n\nError: " + ex.Message,
                            Status = "Failed",
                            SentAt = DateTime.Now,
                            CreatedAt = DateTime.Now
                        });
                    }

                    db.SubmitChanges();
                }
            }
        }
    }
}
