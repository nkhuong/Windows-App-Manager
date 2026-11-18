v0.1.7

Đóng App Manager, giải nén đè toàn bộ source, Ctrl+Shift+B rồi F5 và Refresh. Giữ state.json. Không cần reinstall mRemoteNG/7-Zip nếu Scoop đã cài thành công.

Scoop metadata: ưu tiên scoop-install.json / scoop-manifest.json (Scoop 0.6+), fallback install.json / manifest.json cho các bản đã cài cũ. Áp dụng thống nhất cho Scan, UpdateChecker và kiểm tra quyền sở hữu trước GitHub uninstall. Vẫn bỏ qua marker GitHub, không nhận bừa mọi folder trong Scoop/apps.

Sau install exit code 0, App Manager quét lại và kiểm tra app thực sự được nhận diện. Nếu chưa thấy, báo Installation not confirmed thay vì chỉ dựa vào exit code. Không tự đánh dấu installed để che lỗi quét.

Validation: XML/XAML, event handlers, metadata resolver call sites và ZIP integrity đã kiểm tra. Chưa build/chạy trên Windows trong môi trường này.
