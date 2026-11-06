# Bước 91 — Admin Area và Dashboard

Nhánh phát triển: `feature/admin-dashboard`, bắt đầu từ commit `a9a69b2` của `review/auth-phase1`. Bước này chỉ xây dựng khu vực Tổng quan quản trị; không tạo tài khoản, cấp thêm quyền, tạo migration hoặc triển khai các phân hệ kinh doanh.

## 1. Phạm vi hoạt động

- Dashboard tại `/Admin/Dashboard`, sử dụng ASP.NET Core MVC Area `Admin`.
- Controller yêu cầu `[Authorize(Roles = "Admin")]`; dùng chung hệ thống Identity và cookie đăng nhập hiện có.
- Website khách hàng có thêm liên kết **Trang quản trị** khi người dùng đang đăng nhập có vai trò Admin. Tài khoản có cả Admin và Customer vẫn được nhận diện là Admin.
- Header quản trị hiển thị tài khoản hiện tại, liên kết về website và đăng xuất qua POST có antiforgery.
- Giao diện có layout và `wwwroot/css/admin.css` riêng, dùng nâu `#6F4E37`, lavender `#E6E0F0`, bạc `#DDE3E8`. CSS giao diện khách hàng và công cụ cấp Admin không bị thay thế.

Đường dẫn `/Admin` không phải đường dẫn cần dùng để kiểm tra bước này; dùng chính xác `/Admin/Dashboard`.

## 2. Ý nghĩa dữ liệu Dashboard

Chỉ số **Tài khoản Customer** được truy vấn bất đồng bộ từ dữ liệu Identity hiện có. Chỉ số đếm tài khoản thuộc vai trò Customer, **bao gồm tài khoản đồng thời có vai trò Admin và Customer**. Một tài khoản không được tính nhiều lần.

Đây không phải số người đang trực tuyến, số lượt mua hàng hoặc số người đã thanh toán. Bước này chưa có bảng sản phẩm, đơn hàng hoặc doanh thu để báo cáo các chỉ số đó. Các mục chưa xây dựng thể hiện trạng thái **Sắp triển khai**, **Chưa triển khai** hoặc **Chưa có dữ liệu**, không phải số liệu mẫu.

Nếu truy vấn tài khoản gặp lỗi, Dashboard hiển thị trạng thái chưa tải được dữ liệu thay vì tự hiển thị số 0 hay thông tin exception. Trạng thái đọc dữ liệu chỉ phản ánh lần truy vấn hiện tại, không phải kết luận toàn bộ hệ thống đang hoạt động bình thường.

Sidebar chỉ cho phép đi đến Tổng quan và quay về website. Các mục quản lý khách hàng, danh mục, sản phẩm, kho, đơn hàng, mã giảm giá, báo cáo và cài đặt chưa có liên kết tới chức năng chưa tồn tại. Các thao tác quản lý nhanh chỉ dẫn tới những trang tài khoản đã có.

## 3. Chuẩn bị trên Windows

1. Dùng bản mã Bước 91 đã được bạn xem xét và đồng bộ vào checkout phát triển. Bước phát triển này không tự commit, push hoặc merge; không giả định nhánh đã xuất hiện trên GitHub.
2. Đứng tại thư mục chứa `WEB_BAN_NONG_SAN.csproj`.
3. Kiểm tra đúng SQL Server và database phát triển đã có schema Identity, migration hiện tại và vai trò tương ứng. Không trỏ vào database Production.
4. Dùng `ConnectionStrings:DefaultConnection` trong cấu hình bảo mật hiện có. `UserSecretsId` của project không đổi; không cần in User Secrets hoặc ghi lại bí mật vào repository.
5. Dùng tài khoản Admin đã được bạn xác nhận cấp quyền trên Windows. Nếu phiên đăng nhập có từ trước khi cấp Admin, đăng xuất rồi đăng nhập lại để cookie nhận vai trò mới.

Startup hiện có vẫn gọi `IdentityRoleSeeder` để kiểm tra và tạo vai trò còn thiếu trước khi web lắng nghe. Nó không tự cấp Admin cho bất kỳ tài khoản nào. Vì startup có thể ghi vai trò còn thiếu, chỉ chạy với đúng database phát triển đã được cho phép. Dashboard chỉ đọc dữ liệu; không chạy migration để khắc phục cấu hình kết nối.

## 4. Build và kiểm thử

```powershell
git status --short --branch
dotnet build .\WEB_BAN_NONG_SAN.csproj
if ($LASTEXITCODE -ne 0) { throw "Build that bai; dung lai." }

dotnet test .\tests\WEB_BAN_NONG_SAN.Tests\WEB_BAN_NONG_SAN.Tests.csproj
if ($LASTEXITCODE -ne 0) { throw "Test that bai; dung lai." }
```

Các kiểm thử Dashboard dùng SQLite trong bộ nhớ và TestServer với luồng đăng nhập Identity thật. Chúng kiểm tra routing, authorization, cookie, antiforgery, nội dung thống kê và tình huống lỗi trong môi trường cô lập. Tài khoản và dữ liệu tạo trong test không phải tài khoản hoặc số liệu của website thật.

Kết quả test trên Codex không thay thế kiểm thử tích hợp SQL Server và trình duyệt Windows. Không tự nhận SQL Server hoặc Gmail trên Windows đã được kiểm thử lại chỉ từ các test cô lập. Các bài kiểm thử công cụ Admin trước đó vẫn thuộc bộ test hiện có.

Kết quả thực thi Bước 91 tại Codex:

- Build thành công, **0 cảnh báo, 0 lỗi**.
- **60/60 test đạt**, 0 thất bại, 0 bỏ qua: gồm 44 test CLI cũ và 16 trường hợp HTTP Dashboard/tài khoản mới. Dùng pipeline MVC, cookie/antiforgery và Identity thật trên SQLite in-memory, không thay auth bằng handler giả.
- Kiểm tra quyền anonymous/Customer/Admin, tài khoản có hai vai trò, giá trị Customer bằng 0 hợp lệ, thống kê thay đổi theo dữ liệu test, cache no-store, link quản trị, đăng xuất POST/CSRF, đăng ký vẫn Customer-only, redirect đăng nhập cũ và lỗi truy vấn trong cả Development/Production.
- Chromium kiểm tra HTML Razor được render từ dữ liệu test với tài nguyên cục bộ tại 1440, 1024, 768, 390 và 320 px: không tràn ngang, menu di động mở/đóng được, email dài không vỡ bố cục, không có lỗi JavaScript hoặc asset tải thất bại. Đây là kiểm tra trình bày riêng, không phải phiên trình duyệt kết nối SQL Server thật.
- Chưa kiểm thử SQL Server/Gmail thật hoặc trình duyệt trên Windows. Công cụ cấp Admin, migration, auth controller và CSS khách hàng không đổi.

## 5. Chạy Dashboard trên Windows

PowerShell sau đặt hai biến môi trường tạm thời, sử dụng profile `http` hiện có, và khôi phục giá trị khi ứng dụng kết thúc:

```powershell
$previousAspnetEnvironment = [Environment]::GetEnvironmentVariable(
    "ASPNETCORE_ENVIRONMENT", "Process")
$previousDotnetEnvironment = [Environment]::GetEnvironmentVariable(
    "DOTNET_ENVIRONMENT", "Process")

try {
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DOTNET_ENVIRONMENT = "Development"

    dotnet run --project .\WEB_BAN_NONG_SAN.csproj --no-build --launch-profile http
}
finally {
    [Environment]::SetEnvironmentVariable(
        "ASPNETCORE_ENVIRONMENT", $previousAspnetEnvironment, "Process")
    [Environment]::SetEnvironmentVariable(
        "DOTNET_ENVIRONMENT", $previousDotnetEnvironment, "Process")
}
```

Sau khi terminal báo ứng dụng lắng nghe, mở trình duyệt tại `http://localhost:5083/Account/Login`, đăng nhập bằng tài khoản Admin hiện có rồi dùng liên kết **Trang quản trị** hoặc mở `http://localhost:5083/Admin/Dashboard`.

HTTP loopback ở đây chỉ phục vụ phát triển nội bộ. Nếu máy Windows đang có cấu hình HTTPS khác, dùng địa chỉ của profile đang chạy; không tắt TLS hoặc đưa Development ra Internet. Không truyền `--grant-first-admin` khi chạy Dashboard.

## 6. Checklist kiểm thử thủ công

| Tài khoản / thao tác | Kết quả mong đợi |
| --- | --- |
| Cửa sổ riêng tư, chưa đăng nhập, mở `/Admin/Dashboard` | Chuyển tới Login, không nhận nội dung Dashboard |
| Customer không có Admin, mở Dashboard trực tiếp | Bị từ chối qua trang AccessDenied của cookie hiện có |
| Admin hoặc tài khoản có cả Admin và Customer | Xem được Dashboard và email/tên đăng nhập hiện tại |
| Customer mở trang chủ | Không có liên kết Trang quản trị |
| Admin mở trang chủ | Có liên kết Trang quản trị dẫn tới `/Admin/Dashboard` |
| Admin mở trang khách hàng hoặc hồ sơ | Các trang vẫn truy cập được theo quyền hiện có |
| Chọn đăng xuất trong Admin | Gửi POST có antiforgery; quay về website; mở lại Dashboard phải đăng nhập |
| Gửi POST đăng xuất thiếu antiforgery | Bị từ chối |
| Mục quản lý chưa triển khai | Không có liên kết giả hoặc chuyển tới endpoint chưa tồn tại |
| Desktop, tablet, điện thoại | Sidebar/menu, card và nút không tràn ngang; menu di động đóng/mở được; có focus bàn phím |

Nên kiểm tra giao diện ở các độ rộng khoảng 1440, 1024, 768 và 390 px, cả khi email dài. Dùng tài khoản riêng cho Admin và Customer để đối chiếu quyền; không nâng quyền tài khoản thử mua hàng trong bước này.

Nếu muốn đối chiếu chỉ số trên Dashboard, chọn đúng database phát triển trong SSMS rồi chạy truy vấn **chỉ đọc** với chuẩn hóa role mặc định của Identity:

```sql
SELECT COUNT(DISTINCT ur.UserId) AS CustomerAccountCount
FROM dbo.AspNetUserRoles AS ur
INNER JOIN dbo.AspNetRoles AS r ON r.Id = ur.RoleId
WHERE r.NormalizedName = N'CUSTOMER';
```

Truy vấn trên tính cả tài khoản Admin có Customer, giống ý nghĩa chỉ số. Không sửa role hoặc dữ liệu để làm khớp thống kê; nếu khác nhau, kiểm tra đúng database, thời điểm truy vấn và cấu hình chuẩn hóa role.

## 7. Giới hạn và bước tiếp theo

Ở Codex, chưa có cấu hình SQL Server hoặc SMTP thật để kiểm tra tích hợp của môi trường Windows. Không tạo migration, thay đổi database thật, tạo Admin mới hoặc dùng số liệu sản phẩm/đơn hàng/doanh thu giả trong Bước 91.

Sau khi bạn kiểm tra Dashboard và phân quyền trên Windows, bước tiếp theo đề xuất là thiết kế quản trị tài khoản khách hàng với phân trang, tìm kiếm, xem chi tiết và quy tắc khóa an toàn. Chỉ bắt đầu khi được bạn xác nhận; chưa triển khai CRUD hoặc các phân hệ đó trong Bước 91.
