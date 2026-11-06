# Tài nguyên hình ảnh cho giao diện nông sản

Cập nhật 06/11/2026. Các tài nguyên dưới đây phục vụ nhánh `feature/agriculture-ui-redesign`; không thay đổi dữ liệu bán hàng, Model hoặc Migration.

## Ảnh minh họa được tạo riêng cho nhiệm vụ

Hai ảnh được tạo mới bằng công cụ tạo ảnh trong phiên thiết kế, thay vì sao chép ảnh thương mại từ website khác. Không có phụ thuộc API tạo ảnh hoặc website ảnh bên ngoài khi website chạy; trình duyệt tải file tĩnh trong `wwwroot/images`.

| Tệp | Nội dung | Kích thước | Dung lượng PNG gốc |
|---|---|---|---:|
| `wwwroot/images/agriculture-harvest.png` | Giỏ rau xanh, củ quả và trái cây trên nền kem; dùng cho banner hoặc ảnh dự phòng có nhãn minh họa | 1536 × 1024 | 2.837.925 byte |
| `wwwroot/images/agriculture-field.png` | Khung cảnh ruộng vườn và các luống rau xanh; dùng cho phần giới thiệu nông sản | 1536 × 1024 | 2.994.110 byte |

### Bản phân phối WebP đã tối ưu

Hai tệp `.webp` cùng tên được chuyển mã kỹ thuật bằng ImageMagick (WebP method 6, quality 84), giữ nguyên kích thước, tỷ lệ và bố cục. Không cắt, vẽ lại hoặc thay đổi nội dung. Các View ưu tiên bản WebP thay vì tải PNG gốc dung lượng lớn; PNG giữ làm nguồn tham chiếu.

| Tệp | Kích thước | Dung lượng |
|---|---|---:|
| `wwwroot/images/agriculture-harvest.webp` | 1536 × 1024 | 273.400 byte |
| `wwwroot/images/agriculture-field.webp` | 1536 × 1024 | 329.090 byte |

Ảnh được tạo riêng theo yêu cầu thiết kế và không sử dụng nguồn ảnh thứ ba có giấy phép chưa rõ. Đây không phải ảnh tư liệu chứng minh nguồn gốc hàng hóa, ảnh nông trại của cửa hàng hoặc ảnh sản phẩm thực tế đang được bán. Không gán các giấy phép như CC0/Creative Commons hoặc chứng nhận mà chưa có căn cứ. Quyền sử dụng đầu ra tuân theo điều khoản của công cụ tạo ảnh tại thời điểm tạo; tài liệu này không tuyên bố ảnh được bảo hộ độc quyền.

Không có tên nhà cung cấp, địa danh cụ thể, logo, chữ, nhãn hiệu, VietGAP/GlobalGAP/Organic hoặc người thật trong yêu cầu tạo ảnh. Giao diện không được dùng hai ảnh này để khẳng định cửa hàng sở hữu nông trại hoặc có chứng nhận.

Ảnh do Admin tải lên qua `Product.ImagePath` vẫn là nguồn ưu tiên trên thẻ và chi tiết sản phẩm. Khi dùng ảnh giỏ nông sản dự phòng vì sản phẩm chưa có ảnh, cần hiển thị nhãn **“Ảnh minh họa”** và alt text phân biệt với ảnh thật. Không tự thêm sản phẩm hoặc ảnh vào database.

## Hiển thị

- Giữ tỷ lệ gốc; dùng `object-fit: cover` cho ảnh banner/trang giới thiệu hoặc `contain` khi cần hiển thị toàn bộ nội dung.
- Khai báo width/height để hạn chế dịch chuyển bố cục.
- Banner đầu trang có thể ưu tiên tải; ảnh dưới màn hình và ảnh thẻ sản phẩm dùng lazy loading phù hợp.
- PNG gốc được giữ nguyên byte ảnh tạo ra. Bản WebP đã có để phục vụ web, giảm khoảng 89–90% dung lượng mà không thay tỷ lệ/bố cục; đây là nén có mất dữ liệu, không gọi là bản sao pixel chính xác.
- Không hotlink tài nguyên từ dịch vụ ảnh bên ngoài.

## Logo chính thức do người dùng cung cấp

Ngày 06/11/2026 đã đọc và giải nén thành công `NONG_SAN.zip` do người dùng đính kèm. ZIP chứa đúng một tệp `NONG_SAN.png`; đã mở và đối chiếu hình bông lúa vàng cùng lá xanh tạo thành hình tròn trên nền trắng.

| Tài nguyên | Kích thước | Dung lượng | Nguồn |
|---|---|---:|---|
| `wwwroot/images/logo-nong-san.png` | 1024 × 1024 | 569.019 byte | Bản sao nguyên byte của `NONG_SAN.png` trong ZIP người dùng cung cấp |

```text
logo-nong-san.png
SHA-256 99065c6479ac8e76e23c650b7627d50ea446b90aadff1f4c660824359fda2384
```

Đã đối chiếu byte và SHA-256 giữa tệp trong repository và PNG gốc: trùng khớp. Không vẽ lại, đổi màu, tách nền hoặc cắt nội dung PNG gốc. Tên thương hiệu vẫn là NÔNG SẢN; các vị trí Header/Footer, trang tài khoản và sidebar Admin sử dụng partial nhận diện chung. CSS `object-fit: contain` giữ tỷ lệ ảnh.

### Favicon dẫn xuất từ logo gốc

| Tài nguyên | Kích thước | Dung lượng | SHA-256 |
|---|---|---:|---|
| `wwwroot/images/favicon-nong-san-32.png` | 32 × 32 | 2.569 byte | `01fc638f975bd78c58b853c5d8042bef3f64ca2d09d0409078c490dc66ef3f53` |
| `wwwroot/favicon.ico` | 6 khung: 256, 128, 64, 48, 32, 16 px | 186.951 byte | `3383f3eacbbd88b5e6c0d16f179df8f36cded7b575f67ce32464a568d190d14c` |

Favicon được chuyển đổi kỹ thuật bằng ImageMagick từ chính PNG gốc: nhận diện nền trắng với `fuzz 2%`, thu phần hiển thị vào biên `624×628+201+197`, thêm viền trắng 24px và đặt vào khung vuông 680px, sau đó thu nhỏ thành các kích thước icon. Không vẽ lại, không áp dụng đổi màu hoặc cắt mất chi tiết biểu tượng. Việc thu nhỏ có nội suy; favicon là bản dẫn xuất, không được gọi là trùng byte với PNG gốc.

`_Favicon.cshtml` trỏ tới ICO, PNG 32px có phiên bản cache và Apple touch icon dùng PNG logo gốc. Header/Footer, auth và Admin tiếp tục dùng PNG gốc nguyên trạng, không dùng icon nhỏ để phóng lớn. Kiểm thử MVC xác nhận hash PNG gốc, kích thước PNG favicon và cấu trúc ICO; lượt Chromium cuối đạt 258/258 kiểm tra logo tại 6 kích thước màn hình. ICO/PNG trả HTTP 200, đúng MIME và giải mã thành công; đã kiểm tra sắc xanh/vàng của favicon.

Logo là tài nguyên nhận diện do người dùng cung cấp, khác với hai ảnh minh họa nông sản được tạo riêng bên trên; không tự gán giấy phép CC0 hoặc quyền sở hữu cho logo.

## Kiểm tra tính toàn vẹn PNG gốc

```text
agriculture-harvest.png
SHA-256 2d639c59ab8b42b443ec1d1fa8ebd1a815efca9c153283b5beefc42a491278f4

agriculture-field.png
SHA-256 d0805011c4268400dfc6f3e31d722ae30bfb3f4074c71ef7cb62c7fe3d5f6184
```

Nguồn gốc trong môi trường Cloud: đầu ra công cụ tạo ảnh ngày 06/11/2026, bản gốc được giữ trong `/workspace/generated_images`; tệp đưa vào repository là bản sao nguyên trạng. Không đưa tài khoản, connection string, User Secrets hoặc dữ liệu khách hàng vào ảnh/tài liệu.

## Font tiếng Việt tự phục vụ

- **Be Vietnam Pro**, bốn trọng lượng 400/500/600/700, lấy từ Google Fonts chính thức: `https://github.com/google/fonts/tree/main/ofl/bevietnampro`.
- Giấy phép **SIL Open Font License 1.1** và thông tin tác giả giữ nguyên tại `wwwroot/fonts/be-vietnam-pro/OFL.txt`. Không tự gán giấy phép cho ảnh/logo.
- Các file TTF nguồn được chuyển định dạng WOFF2 để phân phối, không đổi glyph hoặc tên font. Tổng bốn WOFF2 khoảng 160KB; file CSS dùng `font-display: swap`, không gọi Google Fonts/CDN khi chạy website.
- Đã kiểm tra bảng glyph đủ chữ cái, dấu thanh tiếng Việt và ký hiệu đồng Việt Nam. Font áp dụng cho Customer và điều chỉnh font chung tối thiểu của Admin; không sửa nghiệp vụ hoặc bố cục Dashboard/Admin Users.
- CSS `.store-root` đặt cỡ chữ gốc 16px trên điện thoại, tránh quy tắc 14px cũ làm các đơn vị rem bị thu nhỏ.
