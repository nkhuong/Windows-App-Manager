v0.1.10

- Every streamed external command is shown before process startup, with full arguments in copyable PowerShell syntax. The displayed text is never used for execution; ProcessStartInfo.ArgumentList still passes arguments directly.
- CommandResult now stores CommandLine separately from Output. Search and update-check logs show it without adding command text to CLI output parsing.
- Scoop setup logs HTTP GET and the expanded installer invocation. Network download failures retain full error details plus a short explanation and a suggested Test-NetConnection command, explicitly marked as not executed. This does not fix or bypass network restrictions.
- GitHub junction setup shows the readable PowerShell script alongside the actual encoded command.

Validation: XML, call-site checks, and patch checks only. Windows execution not verified.

--- Previous release ---

v0.1.9

Fix Scoop setup "Security error": removed Set-ExecutionPolicy -Scope CurrentUser from the wrapper. The previous wrapper started with Process=Bypass and then tried to set CurrentUser=RemoteSigned. PowerShell can report ExecutionPolicyOverride in this situation; ErrorAction=Stop aborted setup before the Scoop installer ran.

The installer process now requests RemoteSigned directly and does not change the user's saved execution policy. Group Policy restrictions remain in force. The setup log includes effective execution policy, language mode, the full error record, exception details and script position.

Update: close App Manager, apply this patch, Build and copy the complete bin/Debug/net11.0-windows output to the other computer. Run normally, then retry Install Scoop. A folder already containing files must be preserved; choose an empty folder if necessary.

Validation: XML and patch checks passed. Source confirms there is no Set-ExecutionPolicy call in Scoop setup. Windows execution has not been verified in this environment.

--- Previous release ---

v0.1.8

Cập nhật từ Git: đóng App Manager, chạy git pull origin main, mở solution và Build rồi F5. Giữ nguyên state.json.

- Search packages: căn giữa chữ theo chiều dọc trong hàng, thêm khoảng cách ở mép ô và bỏ viền chọn ô mặc định.
- Log install/uninstall/upgrade WinGet và Scoop mở ngay khi bắt đầu, đọc stdout/stderr đồng thời theo từng khối. Log GitHub hiển thị các bước tải, dung lượng đã tải, giải nén và gỡ app. Cửa sổ log giữ mở sau khi hoàn tất; không đóng khi lệnh còn chạy.
- Installed/Planned/Updates/Scoop vẫn bật/tắt bằng lần nhấn tiếp theo, nhưng giữ từ khóa tìm kiếm. Chỉ nút × xóa từ khóa.
- Add/Edit planned: chiều cao theo nội dung, hàng nút riêng và khoảng cách bên dưới để không che Save/Cancel.
- Install Scoop hiện khi chưa tìm thấy scoop.ps1. Cho chọn thư mục trống, chạy installer chính thức https://get.scoop.sh bằng quyền user thường và hiện log trực tiếp. Cài xong kiểm tra file và scoop --version, lưu ScoopRoot trong state.json, áp dụng ngay cho Search/Scan/Backup. Không cần khởi động lại App Manager.
- Cài Scoop đặt PowerShell execution policy CurrentUser thành RemoteSigned và thêm Scoop vào user PATH. Chỉ cài bucket main mặc định; thêm các bucket khác sau nếu cần. Nếu App Manager đang chạy Administrator, mở lại bằng quyền thường trước khi cài.

Kiểm tra: XAML/project XML và git diff --check đạt. Đã kiểm tra rằng chỉ ClearSearchClick xóa từ khóa. Build và thử command streaming trong môi trường tạo code bị auto-review chặn do truy cập mạng metadata/telemetry ngoài phạm vi cần thiết; chưa xác nhận build WPF hoặc cài Scoop thực tế trên Windows.
