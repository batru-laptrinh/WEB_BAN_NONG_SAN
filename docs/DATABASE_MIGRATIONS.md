# Cập nhật database Development an toàn

Mã nguồn phát triển trong repository Cloud. Chưa chạy Migration trên `LENOVO\SQLEXPRESS01`, chưa thay đổi `WEB_BAN_NONG_SAN_DB`. Giữ nguyên `ConnectionStrings:DefaultConnection`, `appsettings.Development.json`, User Secrets và UserSecretsId hiện có. Không tạo database khác để thay thế database đang dùng.

## Migration bổ sung

`20261105155910_AddCommerce`, tiếp sau `20261105050811_InitialIdentity`. Migration chỉ tạo tám bảng mới, các chỉ mục, CHECK và khóa ngoại từ bảng mới; không ALTER/DROP bảng Identity, không thêm/sửa/xóa người dùng, không có dữ liệu seed.

| Bảng | Cột chính |
|---|---|
| Categories | Id, Name, Description, IsActive, Version |
| Products | Id, CategoryId, Name, Description, Unit, Price, Stock, IsActive, IsFeatured, ImagePath, CreatedAtUtc, Version |
| ShippingAddresses | Id, UserId, RecipientName, PhoneNumber, Province, Ward, District (tùy chọn), AddressLine, DeliveryNote, Label, IsDefault, Version |
| CartItems | Id, UserId, ProductId, Quantity, Version |
| Orders | Id (GUID), UserId, CheckoutKey, Status, PaymentStatus, CreatedAtUtc, CompletedAtUtc, Subtotal, ShippingFee, Discount, Total, CouponId, CouponCode, RecipientName, PhoneNumber, Province, Ward, District, AddressLine, DeliveryNote, Version |
| OrderItems | Id, OrderId, ProductId, ProductName, Unit, UnitPrice, Quantity, LineTotal |
| Coupons | Id, Code, IsActive, Kind, Value, MinimumSubtotal, StartsAtUtc, EndsAtUtc, MaxUses, UsedCount, Version |
| CouponUsages | Id, CouponId, OrderId, UserId |

Tiền dùng `decimal(18,2)`, số lượng nguyên. Các khóa ngoại dùng NO ACTION/Restrict: không xóa dây chuyền dữ liệu Identity hoặc đơn hàng. Chỉ mục unique giới hạn một mặc định/người, một dòng giỏ/người/sản phẩm, một đơn/người/CheckoutKey và một lần dùng mã/đơn. `Version` là concurrency token do ứng dụng cập nhật, không phải SQL rowversion. Đơn hàng giữ bản sao thông tin giao hàng, tên/đơn vị/giá sản phẩm; sửa sổ địa chỉ hoặc sản phẩm không sửa đơn đã đặt.

SQL đã sinh và rà soát nằm tại [sql/AddCommerce.sql](sql/AddCommerce.sql). File này chưa được thực thi. Có thể sinh lại bằng lệnh phía dưới để đối chiếu đúng phiên bản đang dùng.

## Bước chỉ đọc trước khi cập nhật

Chỉ thực hiện sau khi mã nguồn được người dùng cho phép đồng bộ vào **chính thư mục Windows hiện có**. Không clone, không `reset --hard`, không `clean`, không ép chuyển nhánh. Giữ nguyên CSS cá nhân và cấu hình cục bộ.

```powershell
Set-Location -LiteralPath 'D:\Học Năm 3\Lập trình Web\WEB_BAN_NONG_SAN'
git status --short --branch
dotnet --version
dotnet tool restore
dotnet build
dotnet test .\tests\WEB_BAN_NONG_SAN.Tests\WEB_BAN_NONG_SAN.Tests.csproj
dotnet ef migrations script 20261105050811_InitialIdentity 20261105155910_AddCommerce --output "$env:TEMP\WEB_BAN_NONG_SAN_AddCommerce.sql"
```

Đọc SQL sinh ra. Không chạy script và `database update` đồng thời. Trong SSMS, kết nối đúng instance, kiểm tra lịch sử Migration, không gửi connection string hoặc nội dung User Secrets cho người khác:

```sql
SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;
SELECT MigrationId FROM [WEB_BAN_NONG_SAN_DB].[dbo].[__EFMigrationsHistory] ORDER BY MigrationId;
```

Dừng nếu lịch sử không có InitialIdentity hoặc schema khác với mã nguồn; không tự chạy lại InitialIdentity trên database có sẵn. Dừng nếu tám bảng mới đã tồn tại mà Migration chưa có trong lịch sử. Chỉ tiếp tục sau khi xác định đúng lịch sử/schema.

## Sao lưu trước khi áp dụng — người dùng thực hiện và xác nhận

Dừng website để tránh ghi đồng thời. Trong SSMS kết nối `LENOVO\SQLEXPRESS01`, thay đường dẫn bằng thư mục **đã tồn tại trên máy SQL Server**, tài khoản dịch vụ SQL Server có quyền ghi. Tên file cần riêng, không ghi đè bản sao cũ. Kiểm tra dung lượng trống trước.

```sql
BACKUP DATABASE [WEB_BAN_NONG_SAN_DB]
TO DISK = N'D:\SQLBackups\WEB_BAN_NONG_SAN_DB_before_commerce_YYYYMMDD_HHMM.bak'
WITH COPY_ONLY, CHECKSUM, STATS = 10;

RESTORE VERIFYONLY
FROM DISK = N'D:\SQLBackups\WEB_BAN_NONG_SAN_DB_before_commerce_YYYYMMDD_HHMM.bak'
WITH CHECKSUM;
```

Đây là lệnh mẫu cho người dùng, chưa được Codex chạy. Không dùng WITH INIT để ghi đè file. VERIFYONLY kiểm tra cấu trúc bản sao, không thay thế thử khôi phục trên môi trường cách ly khi chuẩn bị Production. Sao lưu cả `App_Data\product-images` khi đã có ảnh và kho Data Protection keys đang sử dụng.

## Lệnh cập nhật — CHỈ sau khi người dùng duyệt SQL, sao lưu và cho phép

Đảm bảo môi trường Development và cấu hình `DefaultConnection` vẫn trỏ đúng database/instance ở trên. Không chạy lệnh này như một phần Build/Test hoặc startup:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update 20261105155910_AddCommerce --context ApplicationDbContext
if ($LASTEXITCODE -ne 0) { throw 'Migration thất bại; dừng và kiểm tra, không xóa/tạo lại database.' }
```

Không đổi connection string để vượt lỗi. Công cụ EF sử dụng `ApplicationDbContext` hiện có; không có DbContext hay tài khoản mẫu bổ sung. Không chạy FirstAdmin, migration remove hoặc database update 0.

## Xác minh sau cập nhật

```sql
USE [WEB_BAN_NONG_SAN_DB];
SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT name FROM sys.tables WHERE name IN
('Categories','Products','ShippingAddresses','CartItems','Orders','OrderItems','Coupons','CouponUsages');
SELECT UserId, COUNT(*) AS DefaultsCount FROM dbo.ShippingAddresses
WHERE IsDefault = 1 GROUP BY UserId HAVING COUNT(*) > 1;
SELECT Id FROM dbo.Products WHERE Stock < 0 OR Price < 0;
SELECT UserId, CheckoutKey, COUNT(*) AS Duplicates FROM dbo.Orders
GROUP BY UserId, CheckoutKey HAVING COUNT(*) > 1;
```

Kiểm tra tài khoản/roles vẫn giữ nguyên bằng số lượng và chức năng đăng nhập; không xuất PasswordHash/SecurityStamp/token. Sau đó chạy checklist trong `docs/WINDOWS_ACCEPTANCE.md`. Không có danh mục/sản phẩm/địa chỉ mẫu được tự thêm: Admin tự nhập dữ liệu cần dùng.

## Khôi phục và giới hạn kiểm thử

Migration Down **xóa tám bảng thương mại điện tử cùng dữ liệu bên trong**. Sau khi có đơn hàng, không dùng rollback schema như cách hoàn tác đơn giản. Khi thất bại, dừng ghi, xác định trạng thái Migration rồi lập kế hoạch khôi phục bản sao đã kiểm chứng; không tự động RESTORE đè database thật.

Cloud kiểm thử bằng SQLite cô lập và kiểm tra SQL/metadata SQL Server ngoại tuyến. Điều này chưa chứng minh lock/deadlock, precision, collation, hiệu năng, backup/restore hoặc Migration trên SQL Server Express Windows thật. Cần người dùng kiểm tra trước khi đưa website vào sử dụng.
