# Rà soát giao diện nông sản sau khi tiếp tục nhiệm vụ

Cập nhật 06/11/2026. Tiếp tục trên nhánh đang có `feature/agriculture-ui-redesign`, nền `feature/full-ecommerce` tại `fa35d2e6a6ba394d73aa228558ed8fb7f7cd327f`. Không tạo lại website, branch, DbContext hoặc thư mục Clone Windows; giữ các thay đổi chưa Commit từ trước lúc gián đoạn.

## Phần giao diện đã triển khai

- Header có tìm kiếm GET vào Catalog thật, điều hướng danh mục lấy từ dữ liệu hiện có, giỏ hàng, tài khoản và liên kết Admin theo quyền. Footer chỉ có đường dẫn thực; thông tin liên hệ chỉ hiển thị khi được cấu hình.
- Trang chủ dùng ảnh giỏ rau củ/trái cây và ruộng vườn minh họa, danh mục thật, sản phẩm nổi bật/mới từ database, hướng dẫn đặt hàng và COD. Có trạng thái chưa có danh mục/sản phẩm hoặc không tải được dữ liệu; không seed dữ liệu giả vào database thật.
- Catalog và chi tiết có ảnh lớn, giá VNĐ/đơn vị, trạng thái tồn, bộ lọc và phân trang. Khách đã đăng nhập thêm giỏ bằng POST/CSRF. Khách chưa đăng nhập dùng liên kết Login có ReturnUrl về trang chi tiết GET, tránh quay về URL `/Cart/Add` chỉ nhận POST sau khi đăng nhập.
- Giỏ, Checkout COD, đơn hàng, hồ sơ, địa chỉ và bảy trang Account đồng bộ typography/màu/form/trạng thái. Trường dữ liệu, action, token, xác nhận và logic phía server giữ nguyên.
- Ảnh sản phẩm do Admin tải lên được ưu tiên. Khi chưa có ảnh hoặc ảnh tải lỗi, ảnh dự phòng có nhãn **Ảnh minh họa** và alt phân biệt; cơ chế này không áp dụng lên logo chính thức.
- Font **Be Vietnam Pro** tự phục vụ từ website, bốn trọng lượng 400/500/600/700, giấy phép OFL kèm theo. Đã kiểm tra đủ alphabet, dấu thanh tiếng Việt và ký hiệu ₫; tổng WOFF2 160.048 byte. Cỡ chữ gốc Customer là 16px, không còn bị quy tắc mobile 14px cũ thu nhỏ đơn vị rem.
- Admin chỉ thay phần nhận diện dùng chung và font; không viết lại Dashboard, Admin Users hoặc chức năng quản trị.

## Logo chính thức đã nhận được qua ZIP

Đã đọc `NONG_SAN.zip`, mở đúng ảnh `NONG_SAN.png` bên trong và chép vào `wwwroot/images/logo-nong-san.png`. Tệp PNG 1024 × 1024, 569.019 byte giữ nguyên byte gốc; SHA-256 **99065c6479ac8e76e23c650b7627d50ea446b90aadff1f4c660824359fda2384** đã được đối chiếu trùng khớp độc lập. Đây là đúng logo bông lúa vàng và lá xanh do người dùng cung cấp; không tạo logo thay thế.

Header/Footer, trang tài khoản và sidebar Admin sử dụng cùng partial `_Brand.cshtml`; tên NÔNG SẢN được giữ. PNG gốc không bị đổi màu, vẽ lại hoặc cắt chi tiết; CSS `object-fit: contain` giữ tỷ lệ. Favicon PNG 32px và ICO sáu khung (16/32/48/64/128/256px) chỉ được dẫn xuất kỹ thuật từ ảnh này: cắt khoảng nền trắng, chừa đệm và thu nhỏ, không thiết kế lại. Chi tiết nguồn/hash/quy trình ở [UI_ASSETS.md](UI_ASSETS.md). Lượt Chromium cuối xác nhận logo đúng tài nguyên, kích thước gốc và tỷ lệ trong cả 258 trường hợp; favicon ICO/PNG trả HTTP 200 và giải mã thành công.

## Phạm vi backend và database được bảo toàn

Controllers, Identity, phân quyền, Models, ApplicationDbContext, Migration, Program, công cụ FirstAdmin, SMTP và các nghiệp vụ tồn kho/COD/coupon/order không được sửa trong lần thiết kế này. Hai bổ sung hiển thị là `CartLineViewModel.ImagePath` và một dòng đọc `Product.ImagePath` vào ViewModel giỏ; không thêm cột hoặc thay đổi phép tính/giao dịch.

Không chạy database update, không tạo Migration, không kết nối SQL Server Windows thật, không đổi tài khoản Admin/Customer hoặc User Secrets. File riêng `wwwroot/css/mau-nong-san.css` không có trong Cloud và không được tạo bản thay thế hoặc ghi đè. Người dùng đã xác nhận hai Migration hiện có trên Windows; nhiệm vụ UI không yêu cầu áp dụng lại schema.

## Kiểm chứng khi tiếp tục

Kết quả cuối sau tích hợp PNG logo: Build Debug (web và project kiểm thử) và Release **0 lỗi, 0 cảnh báo**; **299/299 đạt, 0 thất bại, 0 bỏ qua**. Giữ đủ 297 bài trước logo, bổ sung hai bài xác minh HTTP tài nguyên/hash PNG, favicon và nhận diện/điều hướng sidebar Admin. Toàn bộ 293 bài nền vẫn được giữ. Các assertion HTML cũ chỉ thay đổi hợp lý cho ảnh dự phòng mới, phạm vi sản phẩm chính khi có nút mua của sản phẩm liên quan và form mua sau khi đăng nhập; không xóa hoặc Skip bài để che lỗi.

Lệnh đã chạy từ thư mục repository:

```powershell
dotnet build tests/WEB_BAN_NONG_SAN.Tests/WEB_BAN_NONG_SAN.Tests.csproj --no-restore
dotnet test tests/WEB_BAN_NONG_SAN.Tests/WEB_BAN_NONG_SAN.Tests.csproj --no-build
dotnet build WEB_BAN_NONG_SAN.csproj -c Release --no-restore
```

TRX lượt cuối: `/tmp/agriculture-ui-tests/official-logo-final.trx`. Các kiểm thử sử dụng database cô lập, không kết nối database Windows.

Bộ kiểm chứng browser tái sử dụng helper bên ngoài repository tại `/workspace/.cloud-setup/users-review`; không tạo project thử nghiệm mới. Nó render các View thật bằng TestServer/SQLite cô lập, sau đó dùng Chromium xem HTML cùng tài nguyên tĩnh hiện tại. Các ảnh/tài khoản/sản phẩm/đơn trong fixture chỉ là dữ liệu kiểm thử `example.invalid`, không phải dữ liệu kinh doanh thật.

- Export bổ sung ba trang Admin Users: danh sách, chi tiết và xác nhận khóa GET. Không gửi POST khóa/mở khóa tài khoản.
- **62 URL GET nội bộ** lấy từ liên kết được render đã được request qua TestServer, không có phản hồi 4xx/5xx. Chuyển hướng đăng nhập hợp lệ được cho phép; đây là kiểm tra tồn tại route, không thay thế kiểm thử phân quyền.
- Lượt Chromium mới sau tích hợp logo đạt **258/258 trường hợp = 43 biến thể trang × 6 viewport** tại 1440, 1280, 1024, 768, 390 và 320px, gồm cả Dashboard và ba trang Admin Users. Không có tràn ngang, lỗi focus/menu/ARIA/id trùng/font, lỗi JavaScript/console hoặc tài nguyên tải lỗi. Cả **258/258 kiểm tra logo** đạt; kiểm tra ICO/PNG và sắc xanh/vàng đạt. Đã xem ảnh Header desktop và sidebar mobile để đối chiếu trực quan.
- Đã kiểm tra menu/mobile/sidebar/dropdown, điều hướng bằng bàn phím/skip link, target ARIA, id trùng, cỡ chữ gốc, tải font/ảnh, tràn ngang và lỗi tài nguyên. Tình huống cố ý làm hỏng ảnh sản phẩm kiểm tra riêng cũng đạt: ảnh dự phòng WebP cùng origin tải được (naturalWidth 1536), alt an toàn và nhãn **Ảnh minh họa** hiện đúng. Lỗi 404 cố ý không được tính lẫn vào tài nguyên lỗi của các trang bình thường.
- Spot-check màu chữ: nâu/trắng 7,44; xanh đậm/trắng 8,05; chữ phụ/nền kem 5,52; bước mua chưa tới/trắng 4,89. Đây không phải chứng nhận WCAG toàn diện.

Bằng chứng tạm: `store-ui-results.json`, `link-audit.json`, `brand-asset-audit.json`, HTML và PNG cùng thư mục helper. File tạm có thể mất khi môi trường Cloud được tạo lại; bản lưu bàn giao bên dưới chứa kết quả JSON/TRX và ảnh mới. Không đưa chúng vào Git như dữ liệu ứng dụng.

## Ảnh preview

Đã xuất lại ảnh có logo chính thức trong thư mục helper; toàn bộ sản phẩm/tài khoản trong preview thuộc fixture cô lập:

- `official-logo-store-home-guest-1440.png`: trang chủ desktop.
- `official-logo-store-home-guest-390.png`: trang chủ mobile.
- `official-logo-admin-dashboard-1440.png`: Dashboard desktop.
- `official-logo-admin-sidebar-open-390.png`: sidebar Admin mobile đang mở.

Ảnh preview cũ chưa có logo được giữ làm mốc trước tích hợp; không dùng để chứng minh kết quả logo cuối.

Nguồn/phạm vi sử dụng ảnh và font được ghi tại [UI_ASSETS.md](UI_ASSETS.md). Ảnh WebP mới là ảnh minh họa được tạo riêng cho nhiệm vụ, không chứng minh cửa hàng sở hữu nông trại hoặc hàng có chứng nhận. Không có API tạo ảnh hay chức năng AI trong website.

## Còn phải hoàn tất và nghiệm thu

1. Người dùng xem ảnh preview, logo và danh sách file; xác nhận riêng trước khi Commit/Push. **Chưa Stage, Commit, Push hoặc Merge**; HEAD vẫn là commit nền.
2. Sau khi được phép đồng bộ, kiểm tra trình duyệt Windows thật với ảnh sản phẩm và dữ liệu SQL Server hiện có. Cloud chưa kiểm tra hiển thị trên trình duyệt của người dùng, hiệu năng mạng thật hoặc luồng browser end-to-end với SQL Server Windows.
3. Giữ file CSS riêng, cấu hình Development, User Secrets và database hiện có khi đồng bộ trong thư mục Windows gốc. UI không yêu cầu Migration hoặc thay đổi database.

## Danh sách thay đổi chưa Commit

Danh sách cuối có **57 file: 33 file đã theo dõi được chỉnh sửa và 24 file mới**. Giữ toàn bộ 54 file UI trước đó; ba mục bổ sung là PNG gốc, favicon PNG 32px và thay favicon ICO cũ. Trong đợt nhận ZIP này chỉ cập nhật `_Favicon.cshtml`, hai bài test, ba tài liệu và ba tài nguyên logo/favicon; các giao diện còn lại không bị viết lại. HEAD vẫn là commit nền `fa35d2e6a6ba394d73aa228558ed8fb7f7cd327f`; chưa Stage, Commit, Push hoặc Merge phần UI.

### File chỉnh sửa

- `Areas/Admin/Views/Shared/_AdminLayout.cshtml`
- `Services/CheckoutService.cs`
- `ViewModels/CartViewModel.cs`
- `Views/Account/AccessDenied.cshtml`
- `Views/Account/ChangePassword.cshtml`
- `Views/Account/ForgotPassword.cshtml`
- `Views/Account/Login.cshtml`
- `Views/Account/Register.cshtml`
- `Views/Account/ResetPassword.cshtml`
- `Views/Account/ResetPasswordConfirmation.cshtml`
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
- `Views/Home/Index.cshtml`
- `Views/Orders/Details.cshtml`
- `Views/Orders/Index.cshtml`
- `Views/Orders/_OrderContent.cshtml`
- `Views/Profile/Index.cshtml`
- `Views/Shared/Error.cshtml`
- `Views/Shared/_Layout.cshtml`
- `docs/IMPLEMENTATION_PROGRESS.md`
- `tests/WEB_BAN_NONG_SAN.Tests/StorefrontTests.cs`
- `wwwroot/css/addresses.css`
- `wwwroot/css/store.css`
- `wwwroot/favicon.ico`
- `wwwroot/js/site.js`

### File mới

- `ViewComponents/StoreCategoriesViewComponent.cs`
- `Views/Shared/Components/StoreCategories/Default.cshtml`
- `Views/Shared/Components/StoreCategories/Footer.cshtml`
- `Views/Shared/_Brand.cshtml`
- `Views/Shared/_Favicon.cshtml`
- `Views/Shared/_StoreIcons.cshtml`
- `docs/UI_ASSETS.md`
- `docs/UI_REDESIGN_REVIEW.md`
- `tests/WEB_BAN_NONG_SAN.Tests/AgricultureUiTests.cs`
- `wwwroot/css/account.css`
- `wwwroot/css/brand.css`
- `wwwroot/css/catalog.css`
- `wwwroot/css/purchase.css`
- `wwwroot/fonts/be-vietnam-pro/BeVietnamPro-Bold.woff2`
- `wwwroot/fonts/be-vietnam-pro/BeVietnamPro-Medium.woff2`
- `wwwroot/fonts/be-vietnam-pro/BeVietnamPro-Regular.woff2`
- `wwwroot/fonts/be-vietnam-pro/BeVietnamPro-SemiBold.woff2`
- `wwwroot/fonts/be-vietnam-pro/OFL.txt`
- `wwwroot/images/agriculture-field.png`
- `wwwroot/images/agriculture-field.webp`
- `wwwroot/images/agriculture-harvest.png`
- `wwwroot/images/agriculture-harvest.webp`
- `wwwroot/images/favicon-nong-san-32.png`
- `wwwroot/images/logo-nong-san.png`

## Bản lưu bảo vệ tiến độ

Đã chuẩn bị `/workspace/artifacts/agriculture-ui-official-logo-review.zip` bên ngoài repository: đủ 57 file thay đổi, patch nhị phân của file đã theo dõi, manifest SHA-256, bốn ảnh preview mới và kết quả JSON/TRX. Archive được kiểm tra CRC và hash từng file; không chứa `.git`, User Secrets, development config, database hoặc bin/obj. Đây là bản lưu thay đổi chưa Commit, không phải một project hoặc thư mục Clone mới; không tự giải nén/ghi đè lên thư mục Windows của người dùng. Giữ bản tải xuống nếu cần bảo vệ tiến độ ngoài môi trường Cloud.

Danh sách dự kiến Commit và thông điệp đề xuất ở `/workspace/artifacts/agriculture-ui-commit-candidate.txt`; đây chỉ là tài liệu chuẩn bị, chưa chạy lệnh lưu Git. Bản `agriculture-ui-review-pending-logo.zip` được giữ làm mốc trước khi nhận PNG gốc, không phải bản bàn giao cuối. Nhiệm vụ UI không yêu cầu Migration hoặc sửa database; chỉ Commit/Push sau khi người dùng xác nhận riêng.
