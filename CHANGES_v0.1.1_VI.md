# Windows App Manager v0.1.1 – Sửa lỗi build

## Đã sửa
- Thêm `using System.IO;` vào các file dùng `File`, `Directory`, `Path`.
- Không thay đổi tính năng hoặc dữ liệu người dùng.

## Cập nhật project hiện tại
1. Đóng Visual Studio và backup các file bạn đã tự chỉnh.
2. Giải nén ZIP rồi **copy các file code đã sửa** vào `D:\sites\WindowsAppManager`, ghi đè file tương ứng; không cần copy `bin`/`obj`.
3. Mở lại solution rồi chọn **Build > Clean Solution**, sau đó **Build > Rebuild Solution**.
4. Nếu build hiển thị `0 Error(s)`, nhấn `F5`. Khi bị hỏi có muốn chạy last successful build sau lỗi, chọn **No**.

Nếu vẫn có lỗi mới, mở **View > Error List** hoặc **View > Output > Build**, gửi các dòng lỗi đầu tiên.

**Kiểm tra:** Đã kiểm tra XML/XAML/JSON theo cú pháp và xác minh các file sửa, nhưng chưa chạy compiler WPF trên Windows tại môi trường tạo file.
