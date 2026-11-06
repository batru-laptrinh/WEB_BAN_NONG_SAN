using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WEB_BAN_NONG_SAN.Data;
using WEB_BAN_NONG_SAN.Models;

namespace WEB_BAN_NONG_SAN.Tools;

/// <summary>Explicit operator command; never starts the web host or the role seeder.</summary>
public static class FirstAdminCommand
{
    private const string CommandSwitch = "--grant-first-admin";

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Any(arg => arg.Equals(CommandSwitch, StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith(CommandSwitch + "=", StringComparison.OrdinalIgnoreCase));

    public static bool TryGetEmail(IReadOnlyList<string> args, out string email)
    {
        email = string.Empty;
        if (args.Count != 3
            || !args[0].Equals(CommandSwitch, StringComparison.OrdinalIgnoreCase)
            || !args[1].Equals("--email", StringComparison.OrdinalIgnoreCase)
            || args[2].Any(char.IsControl))
        {
            return false;
        }

        var candidate = args[2].Trim();
        if (!IsValidEmail(candidate))
        {
            return false;
        }

        email = candidate;
        return true;
    }

    internal static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= 256
        && !email.Any(char.IsControl)
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address.Equals(email, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            // Do not pass operator arguments to host configuration: --environment/--yes
            // are deliberately unsupported. Read the project's usual Development secrets.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(FirstAdminCommand).Assembly.GetName().Name
            });
            // SQL/Identity errors can contain connection details; keep CLI output controlled.
            builder.Logging.ClearProviders();

            // Fail closed if the two standard environment variables conflict, even
            // when the host's precedence rules would otherwise resolve Development.
            var conflictingEnvironment = new[] { "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT" }
                .Select(Environment.GetEnvironmentVariable)
                .Any(value => !string.IsNullOrWhiteSpace(value)
                    && !value.Equals(Environments.Development, StringComparison.OrdinalIgnoreCase));
            if (!builder.Environment.IsDevelopment() || conflictingEnvironment)
            {
                Console.WriteLine("Từ chối: công cụ chỉ được chạy trong Development.");
                return 3;
            }

            if (!TryGetEmail(args, out var email))
            {
                Console.WriteLine("Cú pháp: --grant-first-admin --email <email tài khoản đã đăng ký>.");
                Console.WriteLine("Không hỗ trợ --yes, tùy chọn môi trường hoặc tham số bổ sung.");
                return 2;
            }

            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine("Thiếu DefaultConnection. Cấu hình SQL Server phát triển an toàn trước khi chạy.");
                return 4;
            }

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString));
            builder.Services.AddIdentityCore<ApplicationUser>(options =>
                options.User.RequireUniqueEmail = true)
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();
            builder.Services.AddScoped<FirstAdminGrantService>();

            await using var app = builder.Build();
            await using var scope = app.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<FirstAdminGrantService>()
                .GrantAsync(email, Console.In, Console.Out);
        }
        catch (Exception)
        {
            Console.WriteLine("Không xác nhận được kết quả cấp quyền. Kiểm tra cấu hình/kết nối và vai trò hiện tại trước khi chạy lại.");
            return 1;
        }
    }
}
