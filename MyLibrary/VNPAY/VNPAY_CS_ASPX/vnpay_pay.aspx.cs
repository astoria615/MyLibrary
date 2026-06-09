using System;
using System.Linq;
using System.Web.UI;
using MyLibrary.Models;
using VNPAY_CS_ASPX;

namespace MyLibrary.VNPAY
{
    public partial class vnpay_pay : System.Web.UI.Page
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!IsPostBack)
            {
                LibraryDataContext db = new LibraryDataContext();
                int userId = int.Parse(User.Identity.Name);
                var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
                if (reader != null)
                {
                    decimal total = db.Fines
                        .Where(f => f.ReaderId == reader.ReaderId && f.PaymentStatus == "Unpaid")
                        .Sum(f => (decimal?)f.Amount) ?? 0;
                    lblAmount.Text = total.ToString("N0");
                }
            }
        }

        protected void btnPay_Click(object sender, EventArgs e)
        {
            LibraryDataContext db = new LibraryDataContext();
            int userId = int.Parse(User.Identity.Name);
            var reader = db.Readers.FirstOrDefault(r => r.UserId == userId);
            long totalFines = 0;
            if (reader != null)
            {
                totalFines = (long)(db.Fines
                    .Where(f => f.ReaderId == reader.ReaderId && f.PaymentStatus == "Unpaid")
                    .Sum(f => (decimal?)f.Amount) ?? 0);
            }

            string bankCode = "";
            if (bankcode_Vnpayqr.Checked) bankCode = "VNPAYQR";
            else if (bankcode_Vnbank.Checked) bankCode = "VNBANK";
            else if (bankcode_Intcard.Checked) bankCode = "INTCARD";

            string locale = locale_En.Checked ? "en" : "vn";

            string vnp_Url = System.Web.Configuration.WebConfigurationManager.AppSettings["vnp_Url"];
            string vnp_Returnurl = System.Web.Configuration.WebConfigurationManager.AppSettings["vnp_Returnurl"];
            string vnp_TmnCode = System.Web.Configuration.WebConfigurationManager.AppSettings["vnp_TmnCode"];
            string vnp_HashSecret = System.Web.Configuration.WebConfigurationManager.AppSettings["vnp_HashSecret"];

            VnPayLibrary vnpay = new VnPayLibrary();
            vnpay.AddRequestData("vnp_Version", "2.1.0");
            vnpay.AddRequestData("vnp_Command", "pay");
            vnpay.AddRequestData("vnp_TmnCode", vnp_TmnCode);
            vnpay.AddRequestData("vnp_Amount", (totalFines * 100).ToString());
            vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
            vnpay.AddRequestData("vnp_CurrCode", "VND");
            vnpay.AddRequestData("vnp_IpAddr", Request.UserHostAddress);
            vnpay.AddRequestData("vnp_Locale", locale);
            vnpay.AddRequestData("vnp_OrderInfo", "Thanh toan don hang:" + DateTime.Now.Ticks.ToString());
            vnpay.AddRequestData("vnp_OrderType", "other");
            vnpay.AddRequestData("vnp_ReturnUrl", vnp_Returnurl);
            vnpay.AddRequestData("vnp_TxnRef", DateTime.Now.Ticks.ToString());
            if (!string.IsNullOrEmpty(bankCode))
                vnpay.AddRequestData("vnp_BankCode", bankCode);

            string paymentUrl = vnpay.CreateRequestUrl(vnp_Url, vnp_HashSecret);
            Response.Redirect(paymentUrl);
        }
    }
}