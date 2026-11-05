# Bước 92 — Admin quản lý tài khoản khách hàng

Nhánh phát triển: `feature/admin-users`, bắt đầu từ commit Dashboard `19f6fe942d82a26b5a27482181b3e0a04a0ed24c`. Chức năng dùng Admin Area, `ApplicationDbContext`, `ApplicationUser` và Identity hiện có. Không thêm model Identity, migration, cơ sở dữ liệu hoặc công cụ cấp quyền.

## 1. Đường dẫn và dữ liệu

| Đường dẫn | Phương thức | Chức năng |
| --- | --- | --- |
| `/Admin/Users` | GET | Danh sách, tìm kiếm và phân trang Customer |
| `/Admin/Users/Details/{id}` | GET | Thông tin cơ bản của Customer |
| `/Admin/Users/Confirm/{id}?operation=lock` | GET | Xác nhận khóa, chưa thay đổi tài khoản |
| `/Admin/Users/Confirm/{id}?operation=unlock` | GET | Xác nhận mở khóa, chưa thay đổi tài khoản |
| `/Admin/Users/Lock/{id}` | POST | Khóa Customer hợp lệ sau xác nhận |
| `/Admin/Users/Unlock/{id}` | POST | Mở khóa Customer hợp lệ sau xác nhận |

Tất cả action yêu cầu vai trò `Admin` trên server. Khách chưa đăng nhập được đưa tới Login; Customer không có Admin bị từ chối. POST thay đổi trạng thái còn yêu cầu antiforgery và phiếu xác nhận hợp lệ. GET không khóa hoặc mở khóa tài khoản.

Danh sách chỉ lấy tài khoản thuộc vai trò Customer qua EF Core/Identity, bao gồm tài khoản có cả Admin và Customer. Một tài khoản xuất hiện một lần. Tài khoản chỉ có Admin không nằm trong phạm vi trang Customer; tài khoản không tồn tại hoặc không có Customer không được xem chi tiết qua chức năng này.

Thông tin hiển thị gồm họ tên, email, số điện thoại, vai trò và trạng thái khóa. Trang không hiển thị PasswordHash, SecurityStamp, ConcurrencyStamp hoặc token nội bộ. Khi không tải được dữ liệu, giao diện báo lỗi an toàn; không dùng số liệu giả thay thế.

Sidebar có liên kết thật tới **Quản lý khách hàng** và đánh dấu mục đang chọn. Dashboard vẫn truy cập tại `/Admin/Dashboard`; các phân hệ chưa xây dựng tiếp tục không có liên kết giả. Giao diện dùng chung `_AdminLayout.cshtml` và `admin.css`, không thay CSS khách hàng.

## 2. Tìm kiếm và phân trang

- Tìm theo họ tên, email hoặc số điện thoại. Chuỗi được bỏ khoảng trắng đầu/cuối, giới hạn 100 ký tự; chuỗi rỗng tương đương không lọc.
- Truy vấn LINQ được EF Core tham số hóa, không ghép SQL từ nội dung người dùng. Cách chuẩn hóa chữ hoa phục vụ tìm kiếm không phân biệt hoa/thường; kết quả dấu tiếng Việt còn phụ thuộc provider và collation của SQL Server thực tế.
- Mỗi trang có 10 tài khoản, đếm và phân trang phía server. Sắp xếp ổn định theo email chuẩn hóa rồi ID. Tham số trang không hợp lệ được giới hạn về phạm vi hợp lệ; bộ lọc được giữ khi chuyển trang và quay lại danh sách.
- Danh sách rỗng hoặc không tìm thấy kết quả có thông báo riêng. Việc sắp xếp ổn định không tạo ảnh chụp dữ liệu giữa các lần tải trang: tài khoản được thêm, xóa hoặc đổi email trong lúc duyệt có thể làm dịch chuyển vị trí.

Cần kiểm tra tìm tên có dấu, không dấu và chữ hoa/thường trên SQL Server Windows. SQLite trong bộ nhớ không mô phỏng đầy đủ quy tắc Unicode/collation của SQL Server; không coi kết quả SQLite là bảo đảm tìm kiếm tiếng Việt trên mọi database.

## 3. Chính sách khóa và mở khóa

| Trạng thái | Ý nghĩa |
| --- | --- |
| Đang hoạt động | Không có lockout đang hiệu lực |
| Khóa tạm thời | Lockout còn hiệu lực với thời điểm kết thúc hữu hạn, ví dụ đăng nhập sai nhiều lần |
| Khóa thủ công | Lockout được bật và `LockoutEnd` là `DateTimeOffset.MaxValue` |

Admin có thể khóa thủ công Customer đang hoạt động hoặc đang khóa tạm thời. Admin có thể mở khóa Customer đang bị khóa. Server kiểm tra lại trạng thái ngay trước khi thay đổi; không chỉ dựa vào nút hiển thị trên trang.

Không cho phép khóa hoặc mở khóa **bất kỳ tài khoản có vai trò Admin**, kể cả Admin đồng thời có Customer. Không cho phép người thực hiện thao tác lên chính mình. Quy tắc bảo vệ mọi Admin cũng bảo vệ Admin cuối cùng; chức năng này không đếm Admin rồi chọn một tài khoản được phép khóa.

Trang xác nhận hiển thị đúng tài khoản đích và thao tác. Phiếu xác nhận được bảo vệ bằng ASP.NET Core Data Protection, hết hạn sau 10 phút và gắn với người thực hiện, ID đích, thao tác cùng phiên bản tài khoản. Phiên bản dùng để kiểm tra đồng thời được giữ bên trong phiếu bảo vệ, không hiển thị giá trị stamp thô. Không sửa ID hoặc đổi thao tác trên một form cũ để áp dụng sang tài khoản khác. Nếu tài khoản thay đổi, cần tải lại thông tin và xác nhận lại.

Trong giao dịch `Serializable`, service kiểm tra lại người thực hiện hiện vẫn là Admin, tài khoản đích vẫn có Customer, không có Admin, không phải chính người thực hiện, phiên bản và trạng thái vẫn phù hợp. Các thay đổi lockout dùng `UserManager`; không chạy SQL UPDATE thủ công.

- Khóa: bật lockout nếu cần, đặt thời điểm kết thúc thành `DateTimeOffset.MaxValue`, cập nhật SecurityStamp.
- Mở khóa: xóa thời điểm kết thúc lockout, đưa bộ đếm đăng nhập thất bại về 0, cập nhật SecurityStamp.
- Mọi `IdentityResult` phải thành công; nếu một bước lỗi thì giao dịch rollback. Không xóa vai trò Customer, cấp Admin hoặc thay đổi mật khẩu.
- Phiếu hết hạn, sai người thực hiện, sai thao tác, tài khoản đã đổi hoặc thao tác lặp lại đều không được dùng để ghi đè trạng thái mới. Lỗi hiển thị bằng tiếng Việt, không trả exception hoặc dữ liệu kết nối ra giao diện.

Giao dịch được thiết kế cho provider quan hệ, bao gồm SQL Server. Kiểm thử SQLite giúp kiểm tra luồng ứng dụng và rollback nhưng không chứng minh hành vi khóa hàng, khóa dải, deadlock hoặc cạnh tranh của SQL Server. Nếu giao dịch báo lỗi hoặc kết quả không rõ do mất kết nối, tải lại trạng thái từ database trước khi xác nhận thao tác mới; không tự gửi lại form cũ.

## 4. Hiệu lực với phiên đăng nhập

Khóa ngăn lần đăng nhập mới ngay khi trạng thái khóa đã được ghi thành công. Cả khóa và mở khóa đều đổi SecurityStamp. Website đặt khoảng kiểm tra SecurityStamp của cookie Identity là **5 phút**.

Phiên đã đăng nhập có thể tiếp tục truy cập trước lần kiểm tra stamp kế tiếp. Validator kiểm tra khi xử lý request sau khi khoảng 5 phút đã trôi qua; không phải bộ hẹn giờ tự đăng xuất đúng giây, và không vô hiệu hóa mọi phiên tức thì. Sau lần kiểm tra phát hiện stamp khác, cookie cũ bị từ chối và người dùng phải đăng nhập lại.

Mở khóa không phục hồi cookie cũ: lần mở khóa đổi stamp lần nữa. Customer dùng mật khẩu hiện tại để đăng nhập lại. Chính sách kiểm tra stamp áp dụng cho cookie Identity của website, không chỉ trang quản trị. Không chỉnh mật khẩu, quyền hoặc dữ liệu SMTP để kiểm tra cơ chế này.

Luồng đổi mật khẩu hiện có được bổ sung kiểm tra trạng thái khóa trước khi thay đổi mật khẩu và làm mới cookie. Nếu tài khoản đang khóa nhưng còn cookie trong khoảng chờ kiểm tra, yêu cầu đó bị đăng xuất và chuyển đến Login; không thể dùng đổi mật khẩu để nhận cookie mới rồi né thu hồi phiên. Đã tái hiện lỗi này trước bản vá và kiểm thử hồi quy cả trường hợp bị khóa lẫn tài khoản đang hoạt động. Thông báo đăng nhập bị khóa dùng lời nhắc chung để không mô tả nhầm khóa thủ công thành lỗi nhập sai mật khẩu.

## 5. Chuẩn bị và chạy trên Windows

1. Dùng duy nhất thư mục gốc `D:\Học Năm 3\Lập trình Web\WEB_BAN_NONG_SAN`. Đồng bộ nhánh tính năng bằng Git ngay trong thư mục này; không clone hoặc tạo bản sao dự án khác.
2. Đứng tại thư mục chứa `WEB_BAN_NONG_SAN.csproj`. Dùng SQL Server **phát triển** đã có schema Identity và vai trò. Kiểm tra cấu hình kết nối trước khi chạy; biến môi trường Development không tự bảo đảm database đích là database phát triển.
3. Giữ `ConnectionStrings:DefaultConnection` trong cấu hình bảo mật/User Secrets hiện có, trỏ tới SQL Server `LENOVO\SQLEXPRESS01`, database `WEB_BAN_NONG_SAN_DB`. Không in User Secrets, mật khẩu hoặc chuỗi kết nối vào báo cáo/Git. Không chạy migration, tạo lại database hoặc cấp lại Admin cho bước này.
4. Dùng Admin đã được kiểm thử trên Windows và Customer thử nghiệm được phép khóa. Không dùng tài khoản mua hàng đang cần giữ phiên đăng nhập hoặc tài khoản thật ngoài phạm vi kiểm thử.

Startup hiện có vẫn kiểm tra/tạo vai trò còn thiếu qua `IdentityRoleSeeder`. Khóa/mở khóa là thao tác ghi dữ liệu có chủ đích; chỉ thực hiện trên đúng database và tài khoản phát triển đã được bạn cho phép. Trong quá trình triển khai trên Codex không chạy công cụ lên SQL Server thật hoặc tự khóa/mở khóa tài khoản của bạn.

### Đồng bộ an toàn ngay trong thư mục gốc

Codex Cloud không trực tiếp truy cập ổ D: trên Windows. Việc commit/push trên Cloud không đồng nghĩa thư mục Windows đã được cập nhật. Quy trình cố định là: phát triển nhánh tính năng, commit/push khi được phép, cập nhật nhánh đó trong thư mục gốc, rồi chỉ kiểm thử SQL Server sau khi người dùng xác nhận đồng bộ xong.

Trước khi chuyển nhánh, mở PowerShell trong VS Code và chạy các lệnh chỉ đọc:

```powershell
Set-Location -LiteralPath 'D:\Học Năm 3\Lập trình Web\WEB_BAN_NONG_SAN'
git status --short --branch
git branch --show-current
git log -3 --oneline
git diff --name-only
git ls-files --others --exclude-standard
git ls-files -- wwwroot/css/mau-nong-san.css appsettings.Development.json
```

Nếu có thay đổi chưa commit hoặc file chưa theo dõi, dừng trước khi switch và xem xét từng tên file. Không dùng `git add .` hoặc commit/stash bí mật để làm working tree sạch. Có thể lưu mã không nhạy cảm bằng commit tạm trên nhánh hiện tại hoặc stash có chọn lọc sau khi kiểm tra; cần ghi lại nhánh/commit hoặc mã stash để khôi phục. Không tự apply/pop stash vào nhánh mới vì có thể xung đột hoặc ghi đè những thay đổi đã hoàn thành. Không dùng `stash -a`, `reset --hard`, `clean -fd` hoặc switch có force/discard.

Đặc biệt bảo vệ `wwwroot/css/mau-nong-san.css` và `appsettings.Development.json`. Nếu cần, chỉ sao lưu các file cục bộ đó sang vị trí riêng ngoài Git với quyền truy cập phù hợp, ghi lại vị trí và đối chiếu nội dung trước khi khôi phục; đó không phải bản sao dự án. User Secrets nằm ngoài repository và `UserSecretsId` không đổi, nên không cần chép hoặc in bí mật vào project.

Ngay cả khi working tree sạch, file CSS đã commit riêng trên nhánh cũ vẫn có thể khác hoặc không có trên nhánh mới. Sau `git fetch origin`, phải đối chiếu diff giữa HEAD và `origin/feature/admin-users`, cùng trạng thái theo dõi của hai file được bảo vệ, trước khi chọn lệnh switch. Nếu refspec chỉ fetch một nhánh khác, có thể fetch rõ nhánh `feature/admin-users` mà không thay đổi working tree. Khi switch, dùng chế độ không ghi đè ignored files (`--no-overwrite-ignore`), không dùng `-C` để ghi đè nhánh cục bộ. Nếu nhánh cục bộ đã có commit riêng hoặc Git báo nguy cơ ghi đè, dừng và kiểm tra thay vì reset.

Không tự ghi đè bản CSS hoặc cấu hình đang có bằng file từ backup/nhánh khác. Sau khi chuyển nhánh an toàn, xác nhận đúng commit được báo sau push, các file cục bộ vẫn còn nguyên và đầy đủ Migrations/FirstAdmin/Dashboard/UsersController; sau đó mới build/test. Không tuyên bố thư mục Windows đã đồng bộ khi chưa có kết quả từ máy Windows. Không kiểm thử khóa/mở khóa tài khoản thật trước khi người dùng cho phép tài khoản thử nghiệm cụ thể.

Chạy Build và toàn bộ bộ kiểm thử từ PowerShell:

```powershell
git status --short --branch
dotnet build .\WEB_BAN_NONG_SAN.csproj
if ($LASTEXITCODE -ne 0) { throw "Build that bai; dung lai." }

dotnet test .\tests\WEB_BAN_NONG_SAN.Tests\WEB_BAN_NONG_SAN.Tests.csproj
if ($LASTEXITCODE -ne 0) { throw "Test that bai; dung lai." }
```

Sau khi kiểm tra đúng kết nối phát triển, chạy profile HTTP hiện có và khôi phục biến môi trường khi kết thúc:

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

Mở `http://localhost:5083/Account/Login`, đăng nhập Admin rồi vào `http://localhost:5083/Admin/Users`. Nếu dùng profile/cổng khác, lấy URL mà ứng dụng đang lắng nghe. HTTP loopback chỉ dành cho phát triển nội bộ; không đưa cấu hình Development lên Internet. Không truyền lệnh FirstAdmin khi chạy website.

## 6. Ma trận kiểm thử trên Windows

| Trường hợp | Kết quả mong đợi |
| --- | --- |
| Chưa đăng nhập mở danh sách, chi tiết hoặc xác nhận | Chuyển tới Login, không nhận dữ liệu Customer |
| Customer không có Admin truy cập URL quản trị hoặc POST trực tiếp | Bị từ chối, không đổi trạng thái tài khoản |
| Admin hoặc Admin+Customer đăng nhập | Xem được danh sách và chi tiết Customer |
| Tài khoản chỉ có Admin; ID không tồn tại; ID không có Customer | Không xuất hiện trong danh sách Customer; không xem/thao tác ngoài phạm vi |
| Tìm họ tên/email/điện thoại, chuỗi trắng, chuỗi dài, không có kết quả | Không lỗi, bộ lọc đúng và được giữ khi chuyển trang |
| Dữ liệu phát triển có trên 10 Customer, email giống nhau hoặc để trống | 10 mục/trang, tổng đúng, thứ tự ổn định theo email chuẩn hóa và ID |
| Trang âm, bằng 0, rất lớn hoặc giá trị không hợp lệ | Được xử lý an toàn trong phạm vi hợp lệ |
| Customer hoạt động: mở xác nhận rồi hủy/quay lại | Không thay đổi tài khoản |
| Khóa Customer được phép và xác nhận rõ email | Khóa thủ công; mật khẩu và vai trò giữ nguyên; đăng nhập mới bị chặn |
| Customer khóa tạm thời do đăng nhập sai | Hiển thị khóa tạm thời; khóa thủ công chuyển sang trạng thái thủ công |
| Mở khóa Customer tạm thời/thủ công | Trở về hoạt động, bộ đếm thất bại về 0, đăng nhập được bằng mật khẩu cũ |
| Nhắm Admin, Admin+Customer hoặc chính người thực hiện bằng URL/POST | Server từ chối cả khóa và mở khóa |
| GET URL Lock/Unlock; POST thiếu antiforgery | Không ghi dữ liệu; request bị từ chối |
| Sửa ID/thao tác, dùng phiếu người khác, phiếu quá 10 phút | Không thay đổi tài khoản |
| Hai Admin mở cùng trạng thái rồi thao tác lần lượt | Form cũ không ghi đè thay đổi mới; cần tải lại và xác nhận lại |
| Tài khoản/role thay đổi hoặc actor mất Admin trước POST | Kiểm tra lại trên server và không áp dụng thao tác không còn hợp lệ |
| Customer đăng nhập ở cửa sổ khác, sau đó bị khóa | Đăng nhập mới bị chặn; cookie cũ bị từ chối ở lần kiểm tra stamp sau khoảng 5 phút |
| Mở khóa sau khi đã khóa, thử lại cookie từ trước khi khóa | Cookie cũ không được phục hồi; cần đăng nhập lại |
| Desktop và điện thoại, họ tên/email dài | Bảng và nút đọc được; không tràn toàn trang; sidebar mở/đóng và focus dùng được |
| Dashboard, đăng ký, đăng nhập, quên mật khẩu, hồ sơ, đăng xuất | Luồng hiện có tiếp tục hoạt động; đăng xuất vẫn POST có antiforgery |

Với kiểm thử hai phiên, dùng hai hồ sơ trình duyệt hoặc cửa sổ riêng tư khác nhau để không ghi đè cookie. Giữ lại thời điểm và thao tác kiểm tra; không ghi token/cookie vào báo cáo. Chỉ kiểm tra luồng quên mật khẩu/SMTP thật nếu đã có cấu hình phát triển được phép; không suy ra SMTP hoạt động từ test cô lập.

## 7. Đối chiếu chỉ đọc bằng SQL Server

Trong SSMS, chọn đúng database phát triển rồi dùng truy vấn chỉ đọc sau để đối chiếu danh sách và trạng thái. Truy vấn không lấy hash mật khẩu hoặc stamp:

```sql
SELECT u.Id, u.FullName, u.Email, u.PhoneNumber,
       u.LockoutEnabled, u.LockoutEnd, u.AccessFailedCount,
       CASE WHEN EXISTS (
           SELECT 1
           FROM dbo.AspNetUserRoles AS adminUr
           INNER JOIN dbo.AspNetRoles AS adminR ON adminR.Id = adminUr.RoleId
           WHERE adminUr.UserId = u.Id AND adminR.NormalizedName = N'ADMIN'
       ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS HasAdminRole
FROM dbo.AspNetUsers AS u
WHERE EXISTS (
    SELECT 1
    FROM dbo.AspNetUserRoles AS customerUr
    INNER JOIN dbo.AspNetRoles AS customerR ON customerR.Id = customerUr.RoleId
    WHERE customerUr.UserId = u.Id AND customerR.NormalizedName = N'CUSTOMER'
)
ORDER BY u.NormalizedEmail, u.Id;
```

Trạng thái khóa phải xét cả `LockoutEnabled`, `LockoutEnd` và thời gian UTC hiện tại. Thời điểm hữu hạn đã qua không còn là khóa đang hiệu lực. Khóa thủ công dùng giá trị cuối cùng của kiểu `DateTimeOffset`; mở khóa đưa `LockoutEnd` về NULL và bộ đếm về 0. Không dùng UPDATE để làm dữ liệu khớp giao diện hoặc thay đổi role qua SSMS.

## 8. Kiểm thử cô lập và giới hạn

Bộ kiểm thử mở rộng dùng Identity, cookie/antiforgery và pipeline MVC thật trên TestServer với SQLite trong bộ nhớ. Các tình huống gồm phân quyền, dữ liệu thật trong database test, tìm kiếm/phân trang, phạm vi Customer, xác nhận, khóa/mở khóa, bảo vệ Admin, đồng thời, rollback và hồi quy tài khoản/Dashboard. Kiểm tra phiên dùng đồng hồ thử nghiệm để đi qua khoảng xác thực stamp 5 phút.

Kết quả dưới đây ghi nhận lần chạy chốt Bước 92. Dữ liệu được tạo trong test không phải dữ liệu website thật.

Kết quả chốt Bước 92 tại Codex: Build **0 lỗi, 0 cảnh báo**; **109/109 test đạt**, 0 thất bại, 0 bỏ qua (60 test cũ và 49 test mới). Đã tái hiện lỗi đổi mật khẩu làm mới cookie của tài khoản bị khóa trước khi sửa; sau bản vá cả kiểm thử ngăn bypass và đổi mật khẩu của Customer hoạt động đều đạt. Đã kiểm tra thực tế việc cookie cũ bị từ chối sau chu kỳ 5 phút bằng đồng hồ kiểm thử, không chờ thời gian thật.

Kiểm tra Chromium trên HTML Razor được render từ SQLite test và tài nguyên cục bộ: 20 tổ hợp của danh sách, chi tiết, xác nhận và Dashboard tại 1440/1024/768/390/320 px đều không tràn ngang; email dài, menu đang chọn, đóng/mở sidebar di động đạt; không có lỗi JavaScript hoặc asset. Đây là kiểm tra trình bày riêng, không phải browser session kết nối SQL Server thật.

Chưa coi SQL Server Windows, collation tiếng Việt, hành vi cạnh tranh của SQL Server, SMTP thật hoặc trình duyệt dùng database thật đã được kiểm thử chỉ từ test SQLite. Cần thực hiện ma trận Windows trên sau khi bạn xem xét mã nguồn. Bước này không tự commit, push, merge, cấp Admin, chạy migration hoặc phát triển phân hệ sản phẩm/đơn hàng.
