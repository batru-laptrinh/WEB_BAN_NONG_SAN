
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace WEB_BAN_NONG_SAN.Services.Email
{
    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public SmtpEmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(
            string toEmail,
            string subject,
            string htmlMessage)
        {
            // Đọc cấu hình SMTP
            string host = GetRequiredSetting("Smtp:Host");
            string username = GetRequiredSetting("Smtp:Username");
            string password = GetRequiredSetting("Smtp:Password");
            string senderEmail = GetRequiredSetting("Smtp:SenderEmail");

            int port = _configuration.GetValue<int>("Smtp:Port", 587);

            // Tạo nội dung email
            var message = new MimeMessage();

            message.From.Add(
                new MailboxAddress("Nông Sản Sạch", senderEmail));

            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            message.Body = new BodyBuilder
            {
                HtmlBody = htmlMessage
            }.ToMessageBody();

            // Kết nối SMTP an toàn
            using var client = new SmtpClient();

            var security = port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await client.ConnectAsync(host, port, security);

            // Xác thực tài khoản gửi email
            await client.AuthenticateAsync(username, password);

            // Gửi email
            await client.SendAsync(message);

            // Ngắt kết nối
            await client.DisconnectAsync(true);
        }

        private string GetRequiredSetting(string key)
        {
            var value = _configuration[key];

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Thiếu cấu hình SMTP: {key}");
            }

            return value;
        }
    }
}
