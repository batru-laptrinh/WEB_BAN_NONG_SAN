
using Microsoft.AspNetCore.Identity;

namespace WEB_BAN_NONG_SAN.Data
{
    public static class IdentityRoleSeeder
    {
        public static async Task SeedRolesAsync(
            IServiceProvider services)
        {
            using var scope = services.CreateScope();

            var roleManager = scope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole>>();

            string[] roles = { "Admin", "Customer" };

            foreach (var roleName in roles)
            {
                // Nếu vai trò đã tồn tại thì bỏ qua
                if (await roleManager.RoleExistsAsync(roleName))
                {
                    continue;
                }

                // Tạo vai trò mới
                var result = await roleManager.CreateAsync(
                    new IdentityRole(roleName));

                if (!result.Succeeded)
                {
                    var errors = string.Join(
                        "; ",
                        result.Errors.Select(e => e.Description));

                    throw new InvalidOperationException(
                        $"Không thể tạo vai trò {roleName}: {errors}");
                }
            }
        }
    }
}
