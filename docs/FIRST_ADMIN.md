# Bước 89 — Cấp quyền Admin đầu tiên

Công cụ cấp thêm vai trò `Admin` cho một tài khoản đã đăng ký, thông qua `UserManager<ApplicationUser>` và `RoleManager<IdentityRole>`. Công cụ không tạo tài khoản, không yêu cầu mật khẩu của tài khoản đích và không xóa vai trò `Customer`.

Chỉ sử dụng thủ công trên máy phát triển, trong môi trường `Development`. Chạy website bình thường không kích hoạt công cụ. Công cụ không mở cổng HTTP, không chạy seeder vai trò và không tạo hoặc áp dụng migration.

## 1. Chuẩn bị trên Windows

1. Sử dụng một tài khoản riêng cho quản trị, khác tài khoản Customer đang kiểm thử mua hàng. Tạo tài khoản đó bằng chức năng đăng ký hiện có của website, rồi kiểm tra đăng nhập thành công.
2. Kiểm tra vai trò `Admin` đã tồn tại trong database. Nếu chưa có, dừng và kiểm tra cấu hình/khởi tạo vai trò hiện tại; công cụ này không tự tạo vai trò.
3. Xác định đúng SQL Server và database phát triển trước khi chạy. Công cụ sẽ dùng `ConnectionStrings:DefaultConnection` của chính project, bao gồm cấu hình .NET User Secrets hiện có khi chạy trong `Development`.
4. Kiểm tra cấu hình kết nối bằng cách an toàn trên máy của bạn. Không chia sẻ chuỗi kết nối, không chạy `dotnet user-secrets list` để đưa toàn bộ bí mật ra terminal và không ghi mật khẩu vào mã nguồn hoặc `appsettings.json`.

Việc đặt tên môi trường là `Development` không chứng minh database đích là database phát triển. Người vận hành phải kiểm tra đúng đích kết nối trước khi xác nhận. Không dùng công cụ với database Production.

## 2. Build

Mở PowerShell tại thư mục chứa `WEB_BAN_NONG_SAN.csproj`:

```powershell
dotnet build .\WEB_BAN_NONG_SAN.csproj
```

Chỉ tiếp tục khi build thành công. Lệnh build không cấp quyền và không áp dụng migration.

## 3. Chạy lệnh cấp Admin thủ công

Đoạn PowerShell sau lưu lại hai biến môi trường đang có và khôi phục chúng sau khi công cụ kết thúc, kể cả khi có lỗi:

```powershell
$previousAspnetEnvironment = [Environment]::GetEnvironmentVariable(
    "ASPNETCORE_ENVIRONMENT", "Process")
$previousDotnetEnvironment = [Environment]::GetEnvironmentVariable(
    "DOTNET_ENVIRONMENT", "Process")
$targetEmail = Read-Host "Email tai khoan quan tri da dang ky"
$commandExitCode = $null

try {
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DOTNET_ENVIRONMENT = "Development"

    dotnet run --project .\WEB_BAN_NONG_SAN.csproj --no-launch-profile -- `
        --grant-first-admin --email $targetEmail

    $commandExitCode = $LASTEXITCODE
}
finally {
    [Environment]::SetEnvironmentVariable(
        "ASPNETCORE_ENVIRONMENT", $previousAspnetEnvironment, "Process")
    [Environment]::SetEnvironmentVariable(
        "DOTNET_ENVIRONMENT", $previousDotnetEnvironment, "Process")
}

Write-Host "Ma ket thuc cong cu: $commandExitCode"
```

`--no-launch-profile` giúp lệnh không dùng cấu hình chạy web trong `launchSettings.json`. Hai biến môi trường chỉ thay đổi trong phiên PowerShell hiện tại và được khôi phục bởi `finally`.

Công cụ kiểm tra email đầu vào, môi trường, vai trò, tài khoản và các Admin hiện có trước khi cho phép cấp quyền. Tài khoản đích đang bị khóa cũng bị từ chối. Nếu một trong hai biến môi trường được đặt rõ thành giá trị khác Development, công cụ dừng kể cả khi biến còn lại là Development. Khi hiển thị email tài khoản đích, đọc kỹ và đối chiếu với tài khoản quản trị riêng của bạn.

Để xác nhận, nhập đúng dòng mà công cụ yêu cầu, theo dạng:

```text
CAP ADMIN <email được công cụ hiển thị>
```

Thay phần trong dấu `<...>` bằng chính email được hiển thị; không nhập dấu ngoặc nhọn. Không dùng pipe hoặc script tự trả lời, không truyền tùy chọn bỏ qua xác nhận. Nếu không chắc chắn, không xác nhận và chạy lại sau khi đã kiểm tra.

Sau xác nhận, công cụ kiểm tra lại điều kiện trong giao dịch trước khi gọi Identity để thêm vai trò. Nếu tài khoản đã là Admin, công cụ thông báo và không thêm trùng. Nếu đã có một Admin khác, công cụ từ chối cấp thêm theo chế độ “Admin đầu tiên”.

## 4. Kết quả và mã kết thúc

| Mã | Ý nghĩa |
| --- | --- |
| `0` | Cấp quyền thành công hoặc tài khoản đích đã có quyền Admin |
| `1` | Thao tác gặp lỗi hoặc không xác nhận được kết quả; kiểm tra lại vai trò trước khi chạy lại |
| `2` | Tham số không hợp lệ hoặc thiếu email |
| `3` | Môi trường không phải Development |
| `4` | Điều kiện chưa đáp ứng, ví dụ thiếu cấu hình, vai trò hoặc tài khoản; hoặc đã có Admin khác |
| `5` | Không xác nhận thao tác |

Nếu thất bại, công cụ không xóa tài khoản hoặc quyền Customer. Không sửa trực tiếp bảng Identity để “khắc phục” lỗi. Nếu kết nối bị ngắt đúng lúc lưu, cần kiểm tra trạng thái vai trò trước khi thử lại thay vì mặc định rằng quyền chưa được cấp.

Sau khi thành công, **đăng xuất và đăng nhập lại** tài khoản đích để cookie đăng nhập nhận vai trò mới. Quyền Admin trong database không tự cập nhật ngay nội dung cookie đang sử dụng.

## 5. Xác minh bằng truy vấn chỉ đọc trong SQL Server

Trong SSMS, chọn đúng database phát triển rồi chạy truy vấn `SELECT` sau. Thay email mẫu bằng email tài khoản đã được bạn xác nhận:

```sql
DECLARE @Email nvarchar(256) = N'<email tài khoản quản trị đã đăng ký>';

SELECT
    u.Id AS UserId,
    u.Email,
    r.Name AS RoleName
FROM dbo.AspNetUsers AS u
LEFT JOIN dbo.AspNetUserRoles AS ur ON ur.UserId = u.Id
LEFT JOIN dbo.AspNetRoles AS r ON r.Id = ur.RoleId
WHERE u.Email = @Email
ORDER BY r.Name;
```

Kết quả mong đợi đối với tài khoản đăng ký thông thường đã được cấp Admin là có hai dòng vai trò `Admin` và `Customer`. Nếu tài khoản chưa có Admin, không sửa dữ liệu bằng SQL; kiểm tra mã kết thúc và điều kiện của công cụ. Có thể dùng cùng truy vấn với email tài khoản Customer thử mua hàng để xác nhận tài khoản đó vẫn chỉ có Customer.

Truy vấn trên chỉ đọc và không hiển thị password hash, token hoặc dữ liệu bí mật. Không chạy `UPDATE`, `DELETE`, rollback migration hoặc `dotnet ef database update` để xác minh bước này.

## 6. Kiểm thử và phạm vi

Chạy các bài kiểm thử cô lập bằng:

```powershell
dotnet test .\tests\WEB_BAN_NONG_SAN.Tests\WEB_BAN_NONG_SAN.Tests.csproj
```

Kiểm thử tự động dùng dữ liệu kiểm thử riêng; không chứng minh công cụ đã hoạt động với SQL Server thật của bạn. Kết quả build/kiểm thử trên Codex phải được phân biệt với kiểm thử tích hợp SQL Server trên Windows.

Kết quả thực thi Bước 89 trên Codex: build thành công với 0 cảnh báo/lỗi; **44/44 kiểm thử đạt, 0 thất bại, 0 bỏ qua**. Trong đó có 8 kiểm thử tiến trình CLI cho môi trường Production/Staging, hai biến môi trường mâu thuẫn và cú pháp không hợp lệ; các kiểm thử Identity còn lại dùng SQLite in-memory cô lập. Đã kiểm tra thiếu role/user, tài khoản đã Admin, hủy/EOF/xác nhận sai, giữ Customer/password hash/security stamp, rollback khi Identity báo lỗi sau khi store đã ghi, email hoặc Admin khác thay đổi lúc chờ xác nhận và thông báo lỗi không lộ chi tiết.

Chưa kiểm thử tích hợp với SQL Server thật, khóa/deadlock giữa nhiều tiến trình trên SQL Server hoặc cập nhật cookie thực tế trên trình duyệt. Công cụ dùng giao dịch Serializable và không giữ giao dịch trong lúc chờ người vận hành xác nhận.

## 7. File của Bước 89

- Sửa `Program.cs`: nhận diện lệnh CLI và thoát trước startup web/seeder.
- Sửa `WEB_BAN_NONG_SAN.csproj`: loại thư mục tests khỏi glob của ứng dụng web; không thêm dependency runtime.
- Tạo `Tools/FirstAdminCommand.cs`: kiểm tra môi trường/tham số, cấu hình DI và đầu ra CLI an toàn.
- Tạo `Tools/FirstAdminGrantService.cs`: kiểm tra tài khoản/vai trò, xác nhận và cấp quyền bằng Identity trong giao dịch.
- Tạo `tests/WEB_BAN_NONG_SAN.Tests/WEB_BAN_NONG_SAN.Tests.csproj`, `FirstAdminCommandTests.cs`, `FirstAdminGrantServiceTests.cs`, `FirstAdminProcessTests.cs`: bộ kiểm thử độc lập.
- Tạo `docs/FIRST_ADMIN.md`: hướng dẫn này.

Không có thao tác cấp quyền lên database thật được thực hiện thay bạn trong bước phát triển này. Bạn xem xét mã nguồn và hướng dẫn trước, sau đó mới chủ động chạy công cụ với tài khoản đích đã xác nhận. Bước 89 không triển khai Dashboard, quản lý người dùng, địa chỉ hoặc các phân hệ bán hàng.
