namespace Orbital.Service;

public class EmailTemplateService
{
    private const string BrandColor = "#6366f1"; // Indigo
    private const string BgColor = "#09090b";     // Zinc 950
    private const string CardBg = "#18181b";      // Zinc 900
    private const string BorderColor = "#27272a";  // Zinc 800
    private const string TextMuted = "#a1a1aa";   // Zinc 400
    private const string TextWhite = "#f4f4f5";   // Zinc 100

    /// <summary>
    /// Wraps raw inner card body inside a bulletproof, responsive email wrapper.
    /// </summary>
    private string WrapInBaseLayout(string title, string innerContent)
    {
        return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{title}</title>
  <style>
    body, table, td, a {{ -webkit-text-size-adjust: 100%; -ms-text-size-adjust: 100%; }}
    table, td {{ mso-table-lspace: 0pt; mso-table-rspace: 0pt; }}
    img {{ -ms-interpolation-mode: bicubic; border: 0; outline: none; text-decoration: none; }}
    body {{ margin: 0; padding: 0; width: 100% !important; background-color: {BgColor}; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }}
    .btn:hover {{ background-color: #4f46e5 !important; }}
  </style>
</head>
<body style=""margin: 0; padding: 0; background-color: {BgColor};"">
  <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""background-color: {BgColor};"">
    <tr>
      <td align=""center"" style=""padding: 40px 16px;"">
        
        <!-- Main Container -->
        <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""max-width: 520px; background-color: {CardBg}; border-radius: 12px; border: 1px solid {BorderColor}; overflow: hidden;"">
          
          <!-- Brand Header -->
          <tr>
            <td style=""padding: 32px 32px 16px 32px; border-bottom: 1px solid {BorderColor};"">
              <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"">
                <tr>
                  <td>
                    <span style=""font-size: 22px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff;"">
                      <span style=""color: {BrandColor};"">●</span> Orbit
                    </span>
                  </td>
                  <td align=""right"">
                    <span style=""display: inline-block; padding: 4px 10px; font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; color: {BrandColor}; background-color: rgba(99, 102, 241, 0.12); border-radius: 20px; border: 1px solid rgba(99, 102, 241, 0.3);"">
                      Secure Auth
                    </span>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- Dynamic Body Content -->
          <tr>
            <td style=""padding: 32px;"">
              {innerContent}
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style=""padding: 24px 32px; background-color: #121214; border-top: 1px solid {BorderColor}; text-align: center;"">
              <p style=""margin: 0; font-size: 12px; color: {TextMuted}; line-height: 1.5;"">
                This is an automated system notification from Orbit Network.<br/>
                If you did not request this, please disregard or secure your credentials.
              </p>
              <p style=""margin: 8px 0 0 0; font-size: 11px; color: #52525b;"">
                &copy; {DateTime.UtcNow.Year} Orbit Platform. All rights reserved.
              </p>
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Email Confirmation Template
    /// </summary>
    public string GenerateEmailConfirmationTemplate(string userHandle, string confirmUrl)
    {
        var body = $@"
          <h1 style=""margin: 0 0 12px 0; font-size: 20px; font-weight: 700; color: {TextWhite}; letter-spacing: -0.3px;"">
            Verify your Orbit handle
          </h1>
          <p style=""margin: 0 0 24px 0; font-size: 14px; line-height: 1.6; color: {TextMuted};"">
            Welcome to Orbit! You registered with the identity handle <strong style=""color: {TextWhite};"">{userHandle}@orbit</strong>. Confirm your email address to activate your handle and begin messaging.
          </p>

          <!-- Action Button -->
          <table role=""presentation"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""margin: 28px 0;"">
            <tr>
              <td align=""center"" style=""border-radius: 8px; background-color: {BrandColor};"">
                <a href=""{confirmUrl}"" target=""_blank"" class=""btn"" style=""display: inline-block; padding: 13px 28px; font-size: 14px; font-weight: 600; color: #ffffff; text-decoration: none; border-radius: 8px; letter-spacing: 0.2px;"">
                  Confirm Account &rarr;
                </a>
              </td>
            </tr>
          </table>

          
          <p style=""margin: 20px 0 0 0; font-size: 12px; color: #71717a;"">
            This verification link will expire in 24 hours.
          </p>";

        return WrapInBaseLayout("Confirm Your Orbit Account", body);
    }

    /// <summary>
    /// Password Reset Template
    /// </summary>
    public string GeneratePasswordResetTemplate(string userHandle, string resetUrl)
    {
        var body = $@"
          <h1 style=""margin: 0 0 12px 0; font-size: 20px; font-weight: 700; color: {TextWhite}; letter-spacing: -0.3px;"">
            Reset your Orbit password
          </h1>
          <p style=""margin: 0 0 24px 0; font-size: 14px; line-height: 1.6; color: {TextMuted};"">
            A password reset was requested for your account <strong style=""color: {TextWhite};"">{userHandle}@orbit</strong>. Click the button below to specify a new password.
          </p>

          <!-- Action Button -->
          <table role=""presentation"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""margin: 28px 0;"">
            <tr>
              <td align=""center"" style=""border-radius: 8px; background-color: {BrandColor};"">
                <a href=""{resetUrl}"" target=""_blank"" class=""btn"" style=""display: inline-block; padding: 13px 28px; font-size: 14px; font-weight: 600; color: #ffffff; text-decoration: none; border-radius: 8px; letter-spacing: 0.2px;"">
                  Reset Password &rarr;
                </a>
              </td>
            </tr>
          </table>

          <div style=""background-color: #27272a; border-left: 3px solid #eab308; padding: 12px 16px; border-radius: 4px; margin: 24px 0;"">
            <p style=""margin: 0; font-size: 12px; color: #fef08a; line-height: 1.4;"">
              <strong>Security Notice:</strong> If you did not make this request, someone may be attempting to access your account. Your password remains unchanged until you click the link.
            </p>
          </div>";

        return WrapInBaseLayout("Reset Your Orbit Password", body);
    }
}