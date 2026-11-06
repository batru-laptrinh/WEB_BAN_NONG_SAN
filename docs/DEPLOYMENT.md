# Chuẩn bị Production và triển khai hosting

Chưa mua hosting/tên miền, chưa triển khai Internet. Cần nghiệm thu SQL Server Windows trước. Website hiện xử lý COD và phí cố định; chưa tích hợp hãng vận chuyển, thanh toán trực tuyến hay AI.

## Máy chủ và cấu hình

- Hosting hỗ trợ ASP.NET Core/.NET 10 và SQL Server phù hợp; cài Hosting Bundle tương ứng nếu dùng IIS. Chạy App Pool tài khoản riêng, quyền tối thiểu; không dùng tài khoản quản trị máy hoặc SQL sysadmin cho ứng dụng.
- HTTPS với chứng chỉ hợp lệ và tên miền chính thức. Đặt `ASPNETCORE_ENVIRONMENT=Production`, không bật Developer Exception Page hoặc sensitive-data logging. `AllowedHosts` phải giới hạn tên miền chính thức bằng biến môi trường/hosting configuration, không dùng `*` khi công khai.
- Giữ bí mật `ConnectionStrings__DefaultConnection`, `Smtp__Host`, `Smtp__Username`, `Smtp__Password`, `Smtp__SenderEmail` trong secret store/hosting configuration; cổng `Smtp__Port` 587 STARTTLS hoặc 465 TLS. Không đặt mật khẩu trong repo, lệnh shell có lịch sử hoặc file publish công khai.
- Cấu hình `App__PublicBaseUrl` bằng HTTPS và tên miền chính thức để liên kết đặt lại mật khẩu dùng nguồn đáng tin cậy, không lấy Host header từ trình duyệt. Giữ cấu hình Windows đã hoạt động khi phát triển.
- Cấu hình SQL Production dùng TLS và kiểm tra chứng chỉ (không TrustServerCertificate để vượt lỗi), firewall chỉ cho máy chủ ứng dụng truy cập, quyền dữ liệu tối thiểu. Tài khoản triển khai Migration tách khỏi tài khoản chạy ứng dụng. Database Windows Development hiện có không bị Codex đổi cấu hình.
- Cấu hình `Commerce__ShippingFee` theo quy tắc kinh doanh đã duyệt. Dữ liệu tiền hai số lẻ; các lượng hàng nguyên theo đơn vị, chưa hỗ trợ cân lẻ.
- Lưu bền vững `App_Data/product-images` ngoài thư mục web công khai, cấp quyền ghi chỉ cho ứng dụng. Sao lưu ảnh cùng SQL, không đưa ảnh người dùng/database/backup vào Git.
- Giữ Data Protection keys bền vững và giới hạn ACL (IIS Load User Profile và thư mục profile ổn định). Nếu nhiều instance, cần cấu hình kho keys chung được mã hóa bằng certificate/secret store và cùng application discriminator. Mã hiện dùng mặc định Data Protection của ASP.NET Core; **chưa có cấu hình nhiều instance**. Không dùng filesystem container tạm làm nơi lưu keys. Mất keys làm cookie và token biểu mẫu không còn hợp lệ.
- Khi có reverse proxy, chỉ tin Forwarded Headers từ IP proxy đã biết, không tin mọi nguồn và không thay HTTPS bằng HTTP công khai. Cấu hình tại hosting theo sơ đồ thực tế; chưa có hạ tầng nên chưa thể xác minh proxy/TLS.

## Build/publish và cập nhật

```powershell
dotnet tool restore
dotnet restore
dotnet build -c Release
dotnet test .\tests\WEB_BAN_NONG_SAN.Tests\WEB_BAN_NONG_SAN.Tests.csproj -c Release
dotnet publish .\WEB_BAN_NONG_SAN.csproj -c Release --no-build -o "$env:TEMP\WEB_BAN_NONG_SAN_publish"
```

Lệnh publish chỉ tạo thư mục artifact tạm, không tạo dự án/clone khác và không đưa lên Internet. Kiểm tra artifact không chứa development config/User Secrets/database/testdata. Không chép artifact đè ảnh hoặc keys đang sử dụng. Dừng ghi trong thời gian cập nhật, backup, review SQL và chỉ áp dụng Migration đã duyệt bằng quy trình riêng. Không có database update tự chạy ở startup. Role seeder cũ chỉ bảo đảm role có sẵn; không tạo tài khoản/đổi quyền người dùng hoặc seed dữ liệu thương mại.

## Trước khi công khai

Nghiệm thu checklist Windows, kiểm tra cạnh tranh SQL Server thật, SMTP thật, cấu hình HTTPS/cookie/host/keys, tải dữ liệu và hiệu năng thực. Rà soát log không thu thập nội dung biểu mẫu/token và quy định lưu/xóa dữ liệu cá nhân. Bổ sung tên pháp nhân, địa chỉ/kênh liên hệ, chính sách đổi trả/giao nhận/bảo vệ dữ liệu đúng với hoạt động thực tế; không tự bịa thông tin doanh nghiệp.

Chuẩn bị backup theo lịch, giám sát lỗi/health ở tầng hosting, thử restore trên môi trường cách ly và có quy trình ứng phó sự cố. Danh mục/sản phẩm/mã giảm giá do Admin tạo, không tự nhập dữ liệu giả. Tài khoản Admin riêng có mật khẩu mạnh; không dùng lại công cụ FirstAdmin nếu đã có Admin. Các bước phụ thuộc máy chủ, tên miền, thông tin pháp lý hoặc bí mật dịch vụ cần người dùng cung cấp/duyệt; không xem tài liệu này là bằng chứng đã triển khai Production.
