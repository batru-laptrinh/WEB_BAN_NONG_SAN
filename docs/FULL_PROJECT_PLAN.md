# Kế hoạch hoàn thiện WEB_BAN_NONG_SAN

Mục tiêu là phát triển tiếp website MVC .NET 10 hiện có trên nhánh `feature/full-ecommerce`, từ nền `feature/admin-users` tại `fbbdfa62b8c5ab43159f4d667df3260e91661278`. Không tạo project hoặc DbContext trùng lặp, không tích hợp AI và không tạo thêm thư mục clone Windows.

## Nền tảng phải bảo toàn

Identity, đăng ký/đăng nhập/đăng xuất, hồ sơ, đổi/khôi phục mật khẩu và SMTP; công cụ Admin đầu tiên; Admin Area/Dashboard; quản lý tài khoản với bảo vệ Admin và Security Stamp. Người dùng đã xác nhận nền tảng này trên Windows với 109 test đạt. Các kiểm chứng bổ sung của cloud phải được ghi riêng trong `IMPLEMENTATION_PROGRESS.md`.

## Bảy giai đoạn thực hiện

| Giai đoạn | Kết quả cần có | Điều kiện kiểm chứng |
|---|---|---|
| 1. Dữ liệu và địa chỉ | Mở rộng ApplicationDbContext bằng bảng thương mại; địa chỉ theo chủ sở hữu, tối đa một mặc định | Ràng buộc/quan hệ/concurrency; kiểm thử chống IDOR/default race; migration tăng dần được xem SQL, không áp dụng DB thật |
| 2. Danh mục/sản phẩm/kho | CRUD phù hợp, ẩn thay vì xóa lịch sử, đơn vị/giá/tồn kho, upload ảnh an toàn | Admin-only, CSRF, validation, phiên bản chống ghi đè, upload loại thật/dung lượng/tên an toàn |
| 3. Giao diện khách hàng | Trang chủ/catalog/chi tiết từ DB, tìm/lọc/sắp xếp/phân trang, đường dẫn tài khoản thống nhất | Không dữ liệu demo; hàng ngừng bán không đặt mới; responsive desktop/tablet/mobile |
| 4. Giỏ hàng và COD | Giỏ theo user; checkout chọn địa chỉ, phí giao hàng cấu hình, ưu đãi, xác nhận tổng server | Token liên kết giỏ/giá/địa chỉ/ưu đãi; transaction; chống trùng đơn; race sản phẩm cuối; không âm kho |
| 5. Đơn hàng | Customer xem/hủy đúng chủ sở hữu; Admin duyệt/giao/hoàn thành/hủy | Máy trạng thái, tách PaymentStatus; snapshot địa chỉ/tên/giá; hoàn kho tối đa một lần |
| 6. Khuyến mãi và báo cáo | Tạo/sửa/bật-tắt mã; giới hạn lượt và thời gian; doanh thu/ngày/tháng/sản phẩm bán chạy | Checkout đánh giá lại trên server, concurrency mã; doanh thu chỉ Completed+Paid, decimal chính xác, không số liệu giả |
| 7. Tích hợp/hoàn thiện | Menu, form, thông báo, CSS nâu–lavender–bạc, tài liệu cài đặt/DB/triển khai | Build/test hồi quy toàn bộ, kiểm thử browser và quyền; phân biệt mã nguồn/cloud/Windows/Internet |

Sau mỗi phần, thực hiện kiểm tra phù hợp, sửa nguyên nhân lỗi, cập nhật tiến độ và tiếp tục phần độc lập tiếp theo. Không dừng chỉ để hỏi các quyết định kỹ thuật thông thường. Không tuyên bố đã kiểm thử SQL Server Windows từ việc test SQLite thành công.

## Thiết kế và các bất biến dữ liệu

- Một `ApplicationDbContext` kế thừa IdentityDbContext hiện có. Models thương mại nằm trong `Models/CommerceModels.cs`; nghiệp vụ nhiều bước ở ShippingAddressService, CheckoutService và OrderService.
- Tiền dùng `decimal` với precision 18,2; không nhận đơn giá/tổng tiền phía client làm nguồn tin cậy. Giới hạn số lượng và ràng buộc không âm ở server và DB.
- `Version` là concurrency token chuỗi được thay sau mỗi mutation; form/token phải đối chiếu phiên bản. Stock, cart, coupon và order cập nhật trong cùng giao dịch khi đặt hàng.
- Đơn lưu snapshot người nhận, địa chỉ, tên/đơn vị/giá sản phẩm và giảm giá tại lúc mua. Không xóa các quan hệ làm mất lịch sử; dùng trạng thái ngừng sử dụng.
- `CheckoutKey` duy nhất theo user chống gửi lại tạo trùng đơn. Hủy đơn đi qua quy tắc trạng thái và concurrency để hoàn kho một lần.
- Mã giảm giá chuẩn hóa ASCII viết hoa; mã đã sử dụng giữ nguyên code/hình thức/giá trị/điều kiện/thời hạn, chỉ đổi bật/tắt và tổng lượt không thấp hơn đã dùng. Không giảm phí giao hàng bằng mã và không giảm quá tiền hàng.
- Doanh thu thực thu là tổng `Order.Total` của đơn Completed + Paid. Chuỗi ngày/tháng dùng ngày hoàn thành UTC+7; sản phẩm bán chạy dùng snapshot OrderItems; số tiền hàng theo dòng không đồng nghĩa doanh thu sau giảm/phí.

## Bảo mật và UI

Admin controllers kiểm tra role trên server; Customer endpoints lấy user từ phiên và lọc owner. POST mutation có antiforgery. Không nâng quyền từ website công khai hoặc nhận role từ client. Không đưa password hash, stamp, token, exception/connection string vào View/log ứng dụng. Quyền Admin đầu tiên không chạy lại.

CSS mới phải hòa hợp #6F4E37 / #E6E0F0 / #DDE3E8. Không thay thế file riêng `mau-nong-san.css` của Windows nếu không có trong cloud. Menu chỉ trỏ endpoint thực tế; các trạng thái rỗng/lỗi phải phân biệt với số 0 hợp lệ. Upload cần kiểm tra byte định dạng, kích thước và không dùng filename/path người gửi.

## Migration và Windows

Chỉ tạo migration bổ sung sau InitialIdentity; không xóa/sửa migration gốc hoặc tự chạy database update. SQL Server thật của người dùng là `LENOVO\SQLEXPRESS01`, DB `WEB_BAN_NONG_SAN_DB` đã có tài khoản; cloud không được kết nối/chỉnh sửa DB đó. Cung cấp hướng dẫn backup, đánh giá schema/SQL, apply và verify để người dùng tự duyệt/thực hiện.

Tiếp tục dùng thư mục Windows chính `D:\Học Năm 3\Lập trình Web\WEB_BAN_NONG_SAN`. Trước mọi đồng bộ phải `git status` và bảo vệ thay đổi chưa lưu; không reset/ghi đè, không tự push/merge. Không copy User Secrets vào Git.

## Kiểm chứng và bàn giao

Chạy `dotnet build WEB_BAN_NONG_SAN.csproj` và `dotnet test tests/WEB_BAN_NONG_SAN.Tests/WEB_BAN_NONG_SAN.Tests.csproj`. Tests dùng dữ liệu cô lập và cần gồm quyền/CSRF/IDOR, validation, upload, tồn kho cuối, đặt trùng, hủy lặp và coupon vượt lượt. Browser kiểm tra route/form/sidebar/menu/cart/checkout trên nhiều viewport nếu công cụ có sẵn.

Kết quả cuối phải tách bốn trạng thái: triển khai mã nguồn; kiểm thử tự động/cloud; tích hợp SQL Server/SMTP/Windows do người dùng xác nhận; triển khai Internet. Hosting/domain/TLS/secrets cần cấu hình thật trước publication; không tự mua hoặc triển khai hạ tầng. Các giới hạn và việc tiếp tục luôn được ghi vào `IMPLEMENTATION_PROGRESS.md`.
