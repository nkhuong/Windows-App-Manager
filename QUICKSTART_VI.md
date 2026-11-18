# Chạy Windows App Manager (bản thử nghiệm v0.1)

**Máy của bạn hiện có .NET SDK 11.0.100**, nên project ZIP này đặt `TargetFramework` là `net11.0-windows`, giúp bạn không cần cài thêm SDK .NET 10.

## Chạy trong Visual Studio 2026

1. **Đóng Visual Studio**, sao lưu folder project WPF đang có tại `D:\sites\WindowsAppManager` (nếu trong đó có code bạn muốn giữ).
2. Giải nén ZIP. Copy **toàn bộ file/folder bên trong** vào `D:\sites\WindowsAppManager\`, đồng ý ghi đè các file template `App.xaml`, `MainWindow.xaml`, `.csproj`.
3. Mở `D:\sites\WindowsAppManager\WindowsAppManager.csproj` bằng Visual Studio.
4. Nhấn **F5** hoặc nút Start để build/chạy.
5. App sẽ quét Registry, Scoop trên `D:\PortableApps\Scoop`, và thử đối chiếu WinGet.

## Thử trước những chức năng an toàn

- **Refresh**: quét danh sách đã cài.
- **Search new app**: tìm ở WinGet hoặc Scoop rồi nhập đúng Package ID khi Add.
- **Add planned**: thêm app chưa cài vào danh sách chuẩn bị cài lại Windows.
- **Check updates**: xem phiên bản mới được các package manager báo.
- **Backup files**: nếu chọn Video Duplicate Finder có profile, app tìm `ScannedFiles.db`, `Settings.json`, `backup.scanresults` ở `persist` hoặc thư mục cài; với app khác sẽ cho chọn file cần backup.

Dữ liệu Planned và backup lưu vào `D:\AppConfig\WindowsAppManager`. Đóng ứng dụng (nhất là VDF/Jellyfin có database đang mở) trước khi backup.

## Chú ý

- `Install`, `Uninstall`, `Upgrade` **thực sự gọi** WinGet hoặc Scoop. App luôn hỏi xác nhận trước khi chạy; hãy thử với một phần mềm không quan trọng.
- `Uninstall` chưa tự động backup; bạn phải bấm **Backup files** trước nếu cần.
- WinGet dùng bảng text để đối chiếu, nên một số app có thể bị thiếu Package ID hoặc hiển thị `Unknown`. Những bản cài có đường dẫn khác nhau không bị gộp.
- Manual Portable có thể thêm bằng **Add planned → Source Manual → nhập folder/EXE hiện có**. Nếu đường dẫn có thật, app hiển thị Installed.
- Version, Size và Install Date đôi khi không có hoặc chỉ là ước tính.
- Đây là MVP; chức năng Restore, GitHub Releases automatic updater, backup profile editor và SQLite là những bước tiếp theo.
- Vì môi trường tạo project không có .NET trên Windows, source chưa được build/F5 trên máy Windows thật. Nếu Visual Studio hiện lỗi Build, gửi dòng **Error List** đầu tiên để sửa chính xác.
