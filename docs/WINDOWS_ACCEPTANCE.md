# Nghiệm thu Windows với SQL Server thật

Chưa thực hiện checklist này trên máy Windows của người dùng. Chỉ chạy sau khi người dùng duyệt mã nguồn, đồng bộ trong thư mục gốc và cho phép cập nhật database theo DATABASE_MIGRATIONS.md. Không dùng tài khoản hoặc đơn hàng đang phục vụ khách thật để kiểm thử cạnh tranh.

1. Sao lưu/kiểm tra backup và lịch sử Migration; áp dụng riêng AddCommerce. Tài khoản/roles cũ giữ nguyên; không cấp lại Admin. Build/Test phải đạt và không xuất bí mật.
2. Đăng nhập Admin/Customer riêng; Customer và khách chưa đăng nhập bị chặn mọi URL Admin, kể cả POST có CSRF hợp lệ của chính Customer. Đăng xuất POST; quên mật khẩu qua SMTP thật, đặt lại và đổi mật khẩu, hồ sơ, khóa/mở khóa ở Admin Users vẫn hoạt động.
3. Địa chỉ: thêm đầu tiên, thêm hai địa chỉ, đổi mặc định, sửa, xóa mặc định và xóa cuối. Hai tài khoản không xem/sửa/xóa/đặt mặc định địa chỉ nhau bằng đổi Id. Quận/huyện tùy chọn; tên hành chính nhập văn bản hiện hành.
4. Admin tạo danh mục/sản phẩm/ảnh PNG hợp lệ; giá âm, tồn âm, file giả/độc hại/quá dung lượng bị từ chối. Tắt danh mục/sản phẩm thì khách không đặt mới. Hai cửa sổ sửa tồn kho/giá dùng phiên bản cũ phải được báo tải lại.
5. Tìm kiếm/lọc/sắp xếp/phân trang; sản phẩm hết hàng hiển thị đúng. Không có sản phẩm thì có trạng thái trống, không có mẫu tự seed. Kiểm tra ảnh/đơn vị/giá và số tiền có phần lẻ.
6. Giỏ: thêm, gộp, sửa/xóa; số lượng vượt tồn hoặc 1.000 bị từ chối; không sửa dòng của người khác. Checkout phải có địa chỉ riêng, tổng tiền server đúng; thay giá/total trên request không làm thay đổi giá thật.
7. Mã: thử hết hạn, chưa bắt đầu, tắt, hết lượt, chưa đủ tiền tối thiểu, phần trăm và cố định; giảm tối đa tiền hàng, phí giao hàng riêng. Sửa điều kiện sau khi đã có lượt bị chặn. Xem lại tổng tiền nếu mã/phí/giá/địa chỉ thay đổi giữa lúc xem và lúc đặt.
8. COD thành công: một Orders, các OrderItems snapshot, trừ tồn/tiêu lượt mã/xóa giỏ đồng thời. Gửi lại cùng request/nhấp hai lần không thêm đơn thứ hai. Đổi địa chỉ/tên/giá sản phẩm sau đặt không đổi bản sao trong chi tiết đơn.
9. Hai khách mua đơn vị tồn cuối đồng thời bằng **hai phiên đăng nhập riêng**: tối đa một đơn được ghi, tồn không âm, bên còn lại thấy yêu cầu kiểm tra lại. Dùng mã còn một lượt tương tự; không vượt MaxUses. Ghi lại lỗi lock/deadlock nếu có; xác minh rollback, tải lại và gửi lại an toàn trước khi kết luận.
10. Hai yêu cầu hủy cùng đơn đồng thời: chỉ hoàn tồn một lần. Customer không hủy sau xác nhận. Admin không nhảy trạng thái, không hủy khi đang giao/hoàn thành; hoàn thành chuyển COD đã thanh toán. Không xem đơn của người khác bằng thay GUID.
11. Báo cáo chỉ tính doanh thu đơn Completed+Paid; đơn đang giao/chờ/hủy không tính. Thống kê ngày/tháng dùng UTC+7; có đơn tiền lẻ và ngày chuyển tháng. Tổng đơn, khách, sản phẩm lấy từ database. Khi database lỗi không hiển thị số 0 giả hoặc exception.
12. Trình duyệt desktop, tablet, điện thoại 320–390px: menu, sidebar, form dài, địa chỉ/tên/email dài, bàn phím focus, validation, ảnh dự phòng và các nút thật. Không có tràn ngang toàn trang hay link `#` giả.

Không gửi log có email/địa chỉ/token/connection string công khai. Ghi rõ kết quả từng mục, phiên bản Windows/SQL Server/.NET, commit, thời gian và vấn đề còn tồn tại. Chỉ công nhận kiểm thử Windows hoàn chỉnh khi người dùng xác nhận các mục này; Cloud không tự điền kết quả thay người dùng.
