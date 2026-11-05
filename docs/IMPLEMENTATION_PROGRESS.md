# Tiến độ triển khai WEB_BAN_NONG_SAN

Cập nhật 05/11/2026. Nhánh `feature/full-ecommerce`, nền `feature/admin-users` tại `fbbdfa62b8c5ab43159f4d667df3260e91661278`. Mã nguồn được lưu bằng Commit cục bộ sau kiểm thử; chưa Push hoặc Merge. Xem hash chính xác bằng `git log -1 --format='%H %s'`. Các nhánh nền giữ nguyên.

## Bốn trạng thái độc lập

| Trạng thái | Kết quả |
|---|---|
| Mã nguồn các chức năng thương mại theo yêu cầu | Đã triển khai và tích hợp bảy giai đoạn |
| Build, kiểm thử tự động và giao diện Cloud | Đạt; số liệu và giới hạn bên dưới |
| Migration, SMTP và nghiệm thu Windows/SQL Server thật | Chưa thực hiện cho phiên bản mới; chờ người dùng duyệt và chạy |
| Triển khai Internet | Chưa thực hiện; cần hosting, tên miền, TLS và cấu hình vận hành |

Không tuyên bố website đã kiểm thử Windows hoàn chỉnh hoặc sẵn sàng Production chỉ từ kết quả Cloud. Không tạo project, DbContext hoặc thư mục clone thay thế.

## Các giai đoạn đã hoàn thành về mã nguồn

| Phần | Chức năng và kiểm chứng |
|---|---|
| Nền Identity/Admin Users | Giữ toàn bộ 109 kiểm thử cũ; Admin/Customer, khóa, stamp, cookie, đăng ký/đăng nhập/hồ sơ vẫn đạt |
| Dữ liệu và địa chỉ | Một DbContext; CRUD owner-scoped; token thao tác; mặc định duy nhất, chuyển/xóa/rollback và yêu cầu đồng thời |
| Danh mục/sản phẩm/ảnh | Admin-only, tìm kiếm/phân trang, validation, version, ẩn thay xóa, quản lý giá/đơn vị/tồn kho; PNG xác minh và dựng lại ngoài wwwroot |
| Storefront | Home, Catalog, chi tiết, tìm/lọc/sắp xếp/phân trang từ DB, trạng thái trống/lỗi/hết hàng, không seed sản phẩm giả |
| Giỏ và COD | Giỏ riêng, giá decimal server, địa chỉ riêng, báo giá có phiên bản, transaction tồn/coupon/đơn/giỏ; idempotency và rollback |
| Đơn hàng | Customer riêng và Admin; máy trạng thái COD, snapshot, hủy hợp lệ, hoàn kho tối đa một lần |
| Khuyến mãi/báo cáo | Mã chuẩn hóa, thời hạn/ngưỡng/giới hạn lượt, điều kiện bất biến sau dùng; số liệu thật, doanh thu Completed+Paid, ngày/tháng UTC+7, top10 snapshot |
| Giao diện/tài liệu | Bộ màu nâu–lavender–bạc, menu thật, responsive, privacy tiếng Việt, tài liệu setup/SQL/backup/nghiệm thu/hosting |

Đường dẫn hoạt động: `/`, `/Catalog`, `/Catalog/Details/{id}`, `/Profile`, `/Addresses`, `/Cart`, `/Checkout`, `/Orders`, `/Orders/Details/{id}`, `/Admin/Dashboard`, `/Admin/Users`, `/Admin/Categories`, `/Admin/Products`, `/Admin/Orders`, `/Admin/Coupons`, `/Admin/Reports`. Các trang riêng yêu cầu đăng nhập; các Controller Admin yêu cầu Admin. POST thay đổi dữ liệu có chống CSRF.

## Kết quả thực tế cuối

- `dotnet build tests/WEB_BAN_NONG_SAN.Tests/WEB_BAN_NONG_SAN.Tests.csproj --no-restore`: thành công, 0 lỗi, 0 cảnh báo.
- `dotnet test ... --no-build`: **293/293 đạt, 0 thất bại, 0 bỏ qua**, gồm toàn bộ 109 kiểm thử nền và 184 mới.
- Build Release thành công; `dotnet publish WEB_BAN_NONG_SAN.csproj -c Release --no-restore --output /tmp/full-commerce-publish` thành công. Artifact kiểm tra không có tests, development config, database, backup hoặc .cloud-setup. Publish chỉ tạo artifact tạm trên Cloud, không triển khai lên máy chủ.
- Chromium: **21 trang Razor thật từ fixture SQLite × 5 viewport = 105 lượt** ở 1440, 1024, 768, 390, 320px. Không tràn ngang toàn trang, lỗi JavaScript hoặc tài nguyên lỗi; kiểm tra menu/sidebar, skip link/focus và tên/email/địa chỉ dài. Đây là UI từ Razor được render bằng database cô lập; không phải browser end-to-end với SQL Server Windows. Nghiệp vụ HTTP/cookie/CSRF được kiểm thử riêng bằng TestServer, gồm một hành trình Admin tạo danh mục/sản phẩm/coupon → Customer tạo địa chỉ/giỏ/đặt COD → Admin hoàn thành → báo cáo.
- SQL Server metadata/GenerateScript kiểm tra ngoại tuyến: snapshot khớp model, tám bảng mới, precision/unique/FK/concurrency và không ALTER Identity. Không mở kết nối SQL Server thật.
- `git diff --check` và kiểm tra staged phải đạt trước Commit; không stage secrets, User Secrets, bin/obj, database, ảnh upload hoặc báo cáo .cloud-setup.

| Bộ kiểm thử | Số đạt |
|---|---:|
| FirstAdminCommand / GrantService / Process | 16 + 20 + 8 |
| AdminDashboard / AdminUsers | 16 + 49 |
| Addresses | 41 |
| CatalogAdmin | 45 |
| Storefront | 15 |
| Checkout | 40 |
| Orders | 20 |
| PromotionsReports | 15 |
| CommerceSchema | 4 |
| CommerceJourney | 4 |
| Tổng | **293** |

Các tình huống có kiểm thử: chưa đăng nhập/Customer vào Admin; IDOR địa chỉ/giỏ/đơn; thiếu CSRF; biểu mẫu sai; ảnh giả/CRC/bom giải nén/path; hết/vượt tồn; hai yêu cầu mua đơn vị cuối; mã còn một lượt; đặt trùng; hủy lặp/đồng thời; thay giá trình duyệt; form cũ/báo giá đổi; rollback khi lưu lỗi; snapshot lịch sử; lỗi database/email. Cạnh tranh thực hiện bằng các kết nối SQLite độc lập; chưa chứng minh lock/deadlock SQL Server thật.

Bằng chứng tạm của phiên Cloud nằm ngoài Git: `/tmp/commerce-test-results/commerce-delivery.trx`, `/workspace/.cloud-setup/users-review/store-ui-results.json` và ảnh cùng thư mục. Tệp tạm có thể không tồn tại khi môi trường được tạo lại; có thể chạy lại bộ test từ repository. Các fixture chỉ sử dụng tài khoản `example.invalid` và dữ liệu cô lập, không dùng tài khoản thật.

## Rà soát và các lỗi đã khắc phục

- Chống shared-cache làm lộ ảnh sản phẩm ẩn: endpoint ảnh dùng `private,no-store`, MIME cố định và nosniff; quyền xem lại ở server.
- Khi mất xác nhận sau lưu ảnh/sản phẩm, giữ ảnh có thể đang được row đã commit tham chiếu; không xóa vì kết quả chưa chắc chắn. Có regression mô phỏng lỗi sau commit. Ảnh mồ côi có thể còn trong thư mục riêng; chỉ dọn sau khi đối chiếu tham chiếu và sao lưu.
- Checkout ràng buộc phiên bản giỏ/sản phẩm/danh mục/địa chỉ/coupon, phí và mức giảm đã xem; thay đổi yêu cầu xem lại. Lặp request đã commit trả đơn của đúng chủ sở hữu.
- CHECK tiền dùng CAST decimal và ROUND phù hợp cả SQL Server và SQLite; phép tính tiền trong ứng dụng vẫn decimal.
- Admin layout nạp jQuery cục bộ trước validation; sửa lỗi JavaScript tìm được bằng Chromium. Không đổi CSS Admin Users/Dashboard cũ hoặc file CSS riêng của người dùng.
- Chỉ sửa tối thiểu chức năng tài khoản: log lỗi SMTP bằng thông báo cố định để không đưa exception chứa bí mật vào log; View quên mật khẩu hiển thị thông báo chung đã được Controller tạo. Kiểm thử chứng minh phản hồi như nhau với email có/không tồn tại và lỗi SMTP không đổi PasswordHash/SecurityStamp. Không viết lại luồng Identity hay SMTP.
- Cookie ngoài Development yêu cầu Secure; có nosniff, Referrer-Policy, chống embedding và CSP giới hạn frame/object/base. Đây không phải thay thế cấu hình HTTPS/hosting đúng.
- Không phát hiện lỗi chặn khác trong phạm vi rà soát hiện tại; đây không phải chứng nhận bảo mật tuyệt đối.

## Migration và tác động dữ liệu

Migration mới duy nhất: **`20261105155910_AddCommerce`** (designer và snapshot đi kèm). Giữ nguyên InitialIdentity.

Tạo `Categories`, `Products`, `ShippingAddresses`, `CartItems`, `Orders`, `OrderItems`, `Coupons`, `CouponUsages`; không sửa bảng Identity, không seed dữ liệu, không thay mật khẩu/roles, không gọi FirstAdmin. Mã startup không có database update/Migrate tự động.

SQL sinh được lưu tại [sql/AddCommerce.sql](sql/AddCommerce.sql), **chưa thực thi**. Quy trình cột/bảng, backup, lệnh update, verify và cảnh báo rollback ở [DATABASE_MIGRATIONS.md](DATABASE_MIGRATIONS.md). Down xóa dữ liệu thương mại; không dùng như hoàn tác an toàn sau khi có đơn hàng.

## Các quyết định kỹ thuật/nghiệp vụ

- COD, phí cố định mặc định 30.000đ/đơn; `Commerce:ShippingFee` cho phép cấu hình 0–1.000.000đ, tối đa hai số lẻ. Không có tích hợp hãng vận chuyển/cổng thanh toán.
- Tiền decimal(18,2). Số lượng nguyên theo đơn vị sản phẩm, tối đa 1.000/dòng và 100 dòng giỏ; sổ địa chỉ tối đa 50/tài khoản.
- Địa chỉ đầu tiên mặc định; xóa mặc định chọn địa chỉ còn lại có Id nhỏ nhất. Nhập địa danh văn bản, quận/huyện tùy chọn.
- Customer hủy khi Pending; Admin hủy khi Pending/Confirmed; Pending → Confirmed → Shipping → Completed/Paid. Đơn hủy hoàn kho một lần và vẫn tính lượt coupon đã dùng.
- Doanh thu tính tổng Total của Completed+Paid, bao gồm phí sau giảm giá; top sản phẩm trình bày tiền hàng trước giảm/phí, dùng snapshot tên/đơn vị.
- Ảnh chỉ PNG RGB/RGBA 8-bit không interlace, tối đa 2MB/2048×2048; JPEG/WebP/SVG chưa hỗ trợ. Không tạo bản thay thế `mau-nong-san.css` của Windows.

## Công việc tiếp theo phụ thuộc người dùng

1. Xem xét Commit cục bộ/nhánh và Migration/SQL. **Chỉ Push sau khi người dùng xác nhận riêng**, không Merge nhánh nền.
2. Đồng bộ sau khi được phép trong chính `D:\Học Năm 3\Lập trình Web\WEB_BAN_NONG_SAN`; giữ CSS cá nhân, development config và User Secrets. Không clone thư mục khác, không reset/clean/force.
3. Người dùng kiểm tra đúng `LENOVO\SQLEXPRESS01` / `WEB_BAN_NONG_SAN_DB`, sao lưu và xác nhận trước khi áp dụng AddCommerce. Codex chưa kết nối/thay đổi database đó.
4. Thực hiện [WINDOWS_ACCEPTANCE.md](WINDOWS_ACCEPTANCE.md), đặc biệt Migration/SMTP/SQL concurrency/rollback/decimal/collation/browser trên database thật. Ghi lại kết quả và sửa vấn đề xác nhận được; không viết lại phần đã có.
5. Trước Internet: cung cấp thông tin cửa hàng/chính sách kinh doanh chính thức, hosting/domain/TLS/secrets, storage ảnh/keys bền vững, cấu hình proxy/ACL, backup/restore, kiểm tra tải và vận hành theo [DEPLOYMENT.md](DEPLOYMENT.md). Chưa công nhận Production hoặc nhiều instance; không mua/triển khai dịch vụ tự động.

## Danh sách file trong thay đổi này

17 file đã theo dõi được sửa, 69 file mới (tổng 86). Các file bin/obj, ảnh thử nghiệm và báo cáo Cloud không thuộc danh sách.

### File chỉnh sửa

- `.gitignore`
- `Areas/Admin/Controllers/DashboardController.cs`
- `Areas/Admin/Views/Dashboard/Index.cshtml`
- `Areas/Admin/Views/Shared/_AdminLayout.cshtml`
- `Controllers/AccountController.cs`
- `Controllers/HomeController.cs`
- `Data/ApplicationDbContext.cs`
- `Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `Program.cs`
- `ViewModels/AdminDashboardViewModel.cs`
- `Views/Account/ForgotPassword.cshtml`
- `Views/Home/Index.cshtml`
- `Views/Home/Privacy.cshtml`
- `Views/Profile/Index.cshtml`
- `Views/Shared/_Layout.cshtml`
- `WEB_BAN_NONG_SAN.csproj`
- `tests/WEB_BAN_NONG_SAN.Tests/AdminDashboardTests.cs`

### File tạo mới

- `.config/dotnet-tools.json`
- `Areas/Admin/Controllers/CategoriesController.cs`
- `Areas/Admin/Controllers/CouponsController.cs`
- `Areas/Admin/Controllers/OrdersController.cs`
- `Areas/Admin/Controllers/ProductsController.cs`
- `Areas/Admin/Controllers/ReportsController.cs`
- `Areas/Admin/Views/Categories/Form.cshtml`
- `Areas/Admin/Views/Categories/Index.cshtml`
- `Areas/Admin/Views/Coupons/Form.cshtml`
- `Areas/Admin/Views/Coupons/Index.cshtml`
- `Areas/Admin/Views/Orders/Details.cshtml`
- `Areas/Admin/Views/Orders/Index.cshtml`
- `Areas/Admin/Views/Products/Form.cshtml`
- `Areas/Admin/Views/Products/Index.cshtml`
- `Areas/Admin/Views/Reports/Index.cshtml`
- `Controllers/AddressesController.cs`
- `Controllers/CartController.cs`
- `Controllers/CatalogController.cs`
- `Controllers/CheckoutController.cs`
- `Controllers/OrdersController.cs`
- `Controllers/ProductImagesController.cs`
- `Data/Migrations/20261105155910_AddCommerce.Designer.cs`
- `Data/Migrations/20261105155910_AddCommerce.cs`
- `Models/CommerceModels.cs`
- `README.md`
- `Services/CheckoutService.cs`
- `Services/OrderService.cs`
- `Services/ProductImageService.cs`
- `Services/ShippingAddressService.cs`
- `ViewModels/AddressFormViewModel.cs`
- `ViewModels/AddressIndexViewModel.cs`
- `ViewModels/CartViewModel.cs`
- `ViewModels/CatalogAdminViewModels.cs`
- `ViewModels/CatalogViewModels.cs`
- `ViewModels/CheckoutViewModel.cs`
- `ViewModels/CouponViewModels.cs`
- `ViewModels/OrderViewModels.cs`
- `ViewModels/ReportViewModels.cs`
- `Views/Addresses/Create.cshtml`
- `Views/Addresses/Delete.cshtml`
- `Views/Addresses/Edit.cshtml`
- `Views/Addresses/Index.cshtml`
- `Views/Addresses/_Form.cshtml`
- `Views/Cart/Index.cshtml`
- `Views/Catalog/Details.cshtml`
- `Views/Catalog/Index.cshtml`
- `Views/Catalog/_ProductCard.cshtml`
- `Views/Checkout/Index.cshtml`
- `Views/Orders/Details.cshtml`
- `Views/Orders/Index.cshtml`
- `Views/Orders/_OrderContent.cshtml`
- `docs/DATABASE_MIGRATIONS.md`
- `docs/DEPLOYMENT.md`
- `docs/FULL_PROJECT_PLAN.md`
- `docs/IMPLEMENTATION_PROGRESS.md`
- `docs/WINDOWS_ACCEPTANCE.md`
- `docs/sql/AddCommerce.sql`
- `tests/WEB_BAN_NONG_SAN.Tests/AddressesTests.cs`
- `tests/WEB_BAN_NONG_SAN.Tests/CatalogAdminTests.cs`
- `tests/WEB_BAN_NONG_SAN.Tests/CheckoutTests.cs`
- `tests/WEB_BAN_NONG_SAN.Tests/CommerceJourneyTests.cs`
- `tests/WEB_BAN_NONG_SAN.Tests/CommerceSchemaTests.cs`
- `tests/WEB_BAN_NONG_SAN.Tests/OrdersTests.cs`
- `tests/WEB_BAN_NONG_SAN.Tests/PromotionsReportsTests.cs`
- `tests/WEB_BAN_NONG_SAN.Tests/StorefrontTests.cs`
- `wwwroot/css/addresses.css`
- `wwwroot/css/commerce.css`
- `wwwroot/css/store.css`
- `wwwroot/images/product-placeholder.svg`
