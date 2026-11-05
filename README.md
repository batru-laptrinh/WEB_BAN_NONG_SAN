# WEB_BAN_NONG_SAN

Website nông sản ASP.NET Core MVC / .NET 10, Identity, EF Core SQL Server, MailKit và xUnit. Phát triển tiếp trên dự án hiện có, một `ApplicationDbContext`, giữ nguyên tài khoản và công cụ FirstAdmin. Không có tích hợp AI, cổng thanh toán trực tuyến hoặc dịch vụ giao vận bên ngoài.

## Chức năng

- Tài khoản, quên/đổi mật khẩu, hồ sơ và Admin Users hiện có.
- Sổ địa chỉ riêng; danh mục/sản phẩm/tồn kho/ảnh PNG do Admin quản lý.
- Trang chủ, tìm kiếm/lọc/sắp xếp/phân trang sản phẩm từ database.
- Giỏ hàng đăng nhập, checkout COD tính giá trên server và chống đặt trùng.
- Customer theo dõi/hủy đơn theo quy tắc; Admin xử lý đơn và thu COD khi hoàn thành.
- Mã giảm giá có thời hạn/điều kiện/hạn mức; Dashboard và báo cáo từ dữ liệu thật.

**Trạng thái kiểm thử chính xác** nằm tại [IMPLEMENTATION_PROGRESS](docs/IMPLEMENTATION_PROGRESS.md). Build/Test Cloud không thay thế nghiệm thu SQL Server Windows. Chưa triển khai Internet.

## Phát triển tại Windows

Sử dụng chính `D:\Học Năm 3\Lập trình Web\WEB_BAN_NONG_SAN`; không tạo bản clone khác. SDK .NET 10 và SQL Server Express hiện có. Giữ nguyên `appsettings.Development.json`, User Secrets, `wwwroot/css/mau-nong-san.css`, `LENOVO\SQLEXPRESS01` và `WEB_BAN_NONG_SAN_DB`.

```powershell
Set-Location -LiteralPath 'D:\Học Năm 3\Lập trình Web\WEB_BAN_NONG_SAN'
dotnet tool restore
dotnet restore
dotnet build
dotnet test .\tests\WEB_BAN_NONG_SAN.Tests\WEB_BAN_NONG_SAN.Tests.csproj
```

Chỉ sau khi duyệt/sao lưu/cho phép áp dụng Migration theo [hướng dẫn database](docs/DATABASE_MIGRATIONS.md), chạy `dotnet run --launch-profile https`. Dùng cấu hình `DefaultConnection` và SMTP hiện có; không đưa chuỗi kết nối, mật khẩu hay User Secrets vào Git. Không tự chạy FirstAdmin lần nữa, không seed sản phẩm/địa chỉ/đơn hàng mẫu.

Các đường dẫn: `/`, `/Catalog`, `/Catalog/Details/{id}`, `/Profile`, `/Addresses`, `/Cart`, `/Checkout`, `/Orders`; Admin: `/Admin/Dashboard`, `/Admin/Users`, `/Admin/Categories`, `/Admin/Products`, `/Admin/Orders`, `/Admin/Coupons`, `/Admin/Reports`. Trang khách hàng yêu cầu đăng nhập cho dữ liệu riêng; toàn bộ Admin yêu cầu role Admin.

## Quy tắc nghiệp vụ

- Số lượng bán nguyên theo đơn vị Admin chọn, tối đa 1.000/dòng; không nhận số kg lẻ. Giỏ tối đa 100 sản phẩm, địa chỉ tối đa 50/tài khoản.
- Tiền dùng decimal với hai số lẻ. Phí vận chuyển cố định mặc định **30.000 đồng/đơn**, cấu hình không bí mật `Commerce:ShippingFee` (0–1.000.000, hai số lẻ). Không nhận có tích hợp hãng vận chuyển.
- Địa chỉ đầu tiên mặc định; đổi mặc định bỏ địa chỉ cũ; xóa mặc định chọn địa chỉ còn lại có Id nhỏ nhất. Nhập địa danh dạng văn bản, không ép theo danh sách hành chính cũ.
- COD: Chờ xác nhận → Đã xác nhận → Đang giao → Hoàn thành/đã thu tiền. Customer chỉ hủy khi chờ; Admin hủy khi chờ/đã xác nhận. Hủy hoàn kho đúng một lần. Lượt dùng mã đã đặt vẫn được tính khi hủy; không hoàn lượt để tránh lạm dụng.
- Thay đổi sản phẩm/địa chỉ/điều kiện mã/phí giao hàng sau khi xem checkout buộc khách xem lại. Đơn lưu bản sao sản phẩm, giá và địa chỉ.
- Ảnh hỗ trợ PNG RGB/RGBA 8-bit, không interlace, tối đa 2 MB và 2048×2048; được xác minh, dựng lại và đặt tên ngẫu nhiên ngoài wwwroot. JPEG/WebP/SVG chưa hỗ trợ. Sao lưu `App_Data/product-images` cùng database. Lỗi xác nhận lưu database có thể để lại ảnh mồ côi; không tự xóa ảnh khi chưa xác minh không còn tham chiếu.

## Tài liệu

- [Kế hoạch tổng thể](docs/FULL_PROJECT_PLAN.md), [tiến độ thực tế](docs/IMPLEMENTATION_PROGRESS.md).
- [Database và Migration](docs/DATABASE_MIGRATIONS.md), [checklist Windows](docs/WINDOWS_ACCEPTANCE.md).
- [Chuẩn bị Production/hosting](docs/DEPLOYMENT.md).
- [FirstAdmin](docs/FIRST_ADMIN.md), [Admin Dashboard](docs/ADMIN_DASHBOARD.md), [Admin Users](docs/ADMIN_USERS.md) là tài liệu các bước đã hoàn thành trước.

Không Push/Merge hoặc cập nhật SQL Server thật trong quá trình phát triển Cloud khi chưa được người dùng cho phép.
