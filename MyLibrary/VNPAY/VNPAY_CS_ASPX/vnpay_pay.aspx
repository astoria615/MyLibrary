<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="vnpay_pay.aspx.cs" Inherits="MyLibrary.VNPAY.vnpay_pay" %>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title>Payment — My Library</title>
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet" />
    <style>
        *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

        body {
            font-family: 'Inter', 'Segoe UI', system-ui, sans-serif;
            background: #0f1117;
            color: #cdd6f4;
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 24px 16px;
        }

        body.light {
            background: #f8fafc;
            color: #1e293b;
        }

        .pay-card {
            background: #1e1e2e;
            border: 1px solid #313244;
            border-radius: 14px;
            width: 100%;
            max-width: 480px;
            overflow: hidden;
            box-shadow: 0 20px 40px rgba(0,0,0,0.4);
        }

        body.light .pay-card {
            background: #ffffff;
            border-color: #e2e8f0;
            box-shadow: 0 4px 24px rgba(0,0,0,0.08);
        }

        .pay-header {
            padding: 24px 28px 20px;
            border-bottom: 1px solid #313244;
            display: flex;
            align-items: center;
            gap: 14px;
        }

        body.light .pay-header {
            border-bottom-color: #e2e8f0;
        }

        .pay-logo {
            font-size: 22px;
            font-weight: 800;
            color: #f5e0dc;
            letter-spacing: -0.5px;
        }

        body.light .pay-logo {
            color: #0f172a;
        }

        .pay-logo span {
            color: #89b4fa;
        }

        .pay-header-sub {
            font-size: 12px;
            color: #7f849c;
            margin-top: 2px;
        }

        .pay-body {
            padding: 24px 28px;
        }

        .amount-box {
            background: #11111b;
            border: 1px solid #313244;
            border-radius: 10px;
            padding: 18px 20px;
            margin-bottom: 22px;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        body.light .amount-box {
            background: #f8fafc;
            border-color: #e2e8f0;
        }

        .amount-label {
            font-size: 12px;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: #7f849c;
        }

        .amount-value {
            font-size: 26px;
            font-weight: 800;
            color: #e24b4a;
        }

        .amount-currency {
            font-size: 14px;
            font-weight: 600;
            margin-left: 4px;
        }

        .section-label {
            font-size: 12px;
            font-weight: 700;
            text-transform: uppercase;
            letter-spacing: 0.5px;
            color: #7f849c;
            margin-bottom: 12px;
        }

        .method-list {
            display: flex;
            flex-direction: column;
            gap: 8px;
            margin-bottom: 22px;
        }

        .method-tile {
            display: flex;
            align-items: center;
            gap: 12px;
            padding: 12px 14px;
            border: 1px solid #313244;
            border-radius: 8px;
            cursor: pointer;
            transition: all 0.15s ease;
            position: relative;
        }

        body.light .method-tile {
            border-color: #e2e8f0;
        }

        .method-tile:hover {
            border-color: #89b4fa;
            background: rgba(137,180,250,0.05);
        }

        .method-tile input[type="radio"] {
            accent-color: #89b4fa;
            width: 16px;
            height: 16px;
            cursor: pointer;
        }

        .method-tile-icon {
            width: 32px;
            height: 32px;
            border-radius: 6px;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 16px;
        }

        .icon-vnpayqr { background: rgba(2,132,199,0.15); }
        .icon-atm { background: rgba(22,163,74,0.15); }
        .icon-intl { background: rgba(124,58,237,0.15); }

        .method-tile-text { flex: 1; }

        .method-tile-name {
            font-size: 13.5px;
            font-weight: 600;
            color: #cdd6f4;
            display: block;
        }

        body.light .method-tile-name {
            color: #1e293b;
        }

        .method-tile-desc {
            font-size: 11px;
            color: #7f849c;
            display: block;
        }

        .locale-row {
            display: flex;
            gap: 10px;
            margin-bottom: 24px;
        }

        .locale-btn {
            flex: 1;
            padding: 9px;
            border: 1px solid #313244;
            border-radius: 7px;
            background: transparent;
            color: #a6adc8;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            transition: all 0.15s ease;
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 6px;
        }

        body.light .locale-btn {
            border-color: #e2e8f0;
            color: #64748b;
        }

        .locale-btn.active {
            border-color: #89b4fa;
            background: rgba(137,180,250,0.1);
            color: #89b4fa;
        }

        .btn-pay {
            width: 100%;
            padding: 13px;
            background: #89b4fa;
            color: #1e1e2e;
            border: none;
            border-radius: 8px;
            font-size: 15px;
            font-weight: 700;
            cursor: pointer;
            transition: all 0.15s ease;
            letter-spacing: 0.2px;
        }

        .btn-pay:hover {
            background: #b4d0ff;
            transform: translateY(-1px);
        }

        .btn-cancel {
            width: 100%;
            padding: 11px;
            background: transparent;
            color: #7f849c;
            border: 1px solid #313244;
            border-radius: 8px;
            font-size: 13px;
            font-weight: 500;
            cursor: pointer;
            margin-top: 10px;
            transition: all 0.15s ease;
        }

        body.light .btn-cancel {
            border-color: #e2e8f0;
        }

        .btn-cancel:hover {
            color: #cdd6f4;
            border-color: #7f849c;
        }

        .secure-badge {
            text-align: center;
            margin-top: 18px;
            font-size: 11.5px;
            color: #7f849c;
        }

        .secure-badge span {
            color: #a6e3a1;
            font-weight: 600;
        }
    </style>
</head>
<body id="payBody">
<form id="form1" runat="server">
    <div class="pay-card">
        <div class="pay-header">
            <div>
                <div class="pay-logo">MY <span>LIBRARY</span></div>
                <div class="pay-header-sub">Secure Fine Payment Portal</div>
            </div>
        </div>

        <div class="pay-body">
            <div class="amount-box">
                <div>
                    <div class="amount-label">Outstanding Balance</div>
                    <div style="font-size:12px;color:#7f849c;margin-top:2px;">Library fines due</div>
                </div>
                <div>
                    <span class="amount-value">
                        <asp:Label ID="lblAmount" runat="server" Text="0"></asp:Label>
                    </span>
                    <span class="amount-currency" style="color:#e24b4a;">VND</span>
                </div>
            </div>

            <div class="section-label">Payment Method</div>
            <div class="method-list">
                <label class="method-tile">
                    <asp:RadioButton ID="bankcode_Default" GroupName="BankCode" Checked="true" runat="server" />
                    <div class="method-tile-icon icon-vnpayqr">💎</div>
                    <div class="method-tile-text">
                        <span class="method-tile-name">VNPay Gateway</span>
                        <span class="method-tile-desc">All methods — choose at VNPay</span>
                    </div>
                </label>
                <label class="method-tile">
                    <asp:RadioButton ID="bankcode_Vnpayqr" GroupName="BankCode" runat="server" />
                    <div class="method-tile-icon icon-vnpayqr">📱</div>
                    <div class="method-tile-text">
                        <span class="method-tile-name">VNPay QR</span>
                        <span class="method-tile-desc">Scan QR via banking app</span>
                    </div>
                </label>
                <label class="method-tile">
                    <asp:RadioButton ID="bankcode_Vnbank" GroupName="BankCode" runat="server" />
                    <div class="method-tile-icon icon-atm">🏦</div>
                    <div class="method-tile-text">
                        <span class="method-tile-name">Domestic ATM / Bank Account</span>
                        <span class="method-tile-desc">Local debit card or internet banking</span>
                    </div>
                </label>
                <label class="method-tile">
                    <asp:RadioButton ID="bankcode_Intcard" GroupName="BankCode" runat="server" />
                    <div class="method-tile-icon icon-intl">💳</div>
                    <div class="method-tile-text">
                        <span class="method-tile-name">International Card</span>
                        <span class="method-tile-desc">Visa, Mastercard, JCB</span>
                    </div>
                </label>
            </div>

            <div class="section-label">Language</div>
            <div class="locale-row">
                <asp:RadioButton ID="locale_Vn" GroupName="Locale" Checked="true" runat="server" style="display:none" />
                <asp:RadioButton ID="locale_En" GroupName="Locale" runat="server" style="display:none" />
                <button type="button" class="locale-btn active" id="btn_vn" onclick="setLocale('vn')">🇻🇳 Tiếng Việt</button>
                <button type="button" class="locale-btn" id="btn_en" onclick="setLocale('en')">🇬🇧 English</button>
            </div>

            <asp:Button ID="btnPay" runat="server" Text="💳  Proceed to Payment" CssClass="btn-pay" OnClick="btnPay_Click" />
            <button type="button" class="btn-cancel" onclick="history.back()">← Cancel & Go Back</button>

            <div class="secure-badge">🔒 <span>SSL Encrypted</span> · Powered by VNPay Sandbox</div>
        </div>
    </div>
</form>

<script>
    // Sync dark/light mode from main site cookie or localStorage
    (function () {
        try {
            var theme = localStorage.getItem('theme') ||
                (document.cookie.match(/darkMode=([^;]+)/) || [])[1];
            if (theme === 'light') {
                document.body.classList.add('light');
            }
        } catch (e) {}
    })();

    function setLocale(lang) {
        document.getElementById('btn_vn').classList.toggle('active', lang === 'vn');
        document.getElementById('btn_en').classList.toggle('active', lang === 'en');
        // sync hidden radio
        document.getElementById('<%= locale_Vn.ClientID %>').checked = (lang === 'vn');
        document.getElementById('<%= locale_En.ClientID %>').checked = (lang === 'en');
    }
</script>
</body>
</html>