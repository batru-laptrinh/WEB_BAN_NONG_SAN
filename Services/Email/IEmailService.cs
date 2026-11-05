
namespace WEB_BAN_NONG_SAN.Services.Email
{
    public interface IEmailService
    {
        Task SendEmailAsync(
            string toEmail,
            string subject,
            string htmlMessage);
    }
}
