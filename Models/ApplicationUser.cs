using Microsoft.AspNetCore.Identity;

namespace WEB_BAN_NONG_SAN.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? FullName { get; set; } = string.Empty;

    }
}