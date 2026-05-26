# Quy tắc AI cho dự án này (AI Rules For This Project)

## Phạm vi áp dụng
- Áp dụng các quy tắc này cho tất cả thay đổi mã nguồn trong kho lưu trữ này.
- Thứ tự ưu tiên: tính chính xác, tính nhất quán và thực thi an toàn.

## Quy trình bắt buộc (Luôn luôn thực hiện)
1. Cưỡng chế đóng phiên bản ứng dụng cũ đang chạy nếu cần.
2. Build ứng dụng ở cấu hình `Release`.
3. Nếu có bất kỳ cảnh báo (warning) hoặc lỗi (error) nào, phải sửa sạch cho đến khi hết hoàn toàn.
4. Chạy file `bin\Release\MKLink.exe` mới build để kiểm tra thực tế.
5. Sau khi hoàn tất thay đổi mã nguồn, phải `git commit` rồi `git push` lên nhánh hiện tại trước khi báo cáo hoàn thành.

## Lệnh Build
- Sử dụng:
  - `C:\Program Files (x86)\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe MKLink.csproj /p:Configuration=Release /p:Platform=AnyCPU`

## Các quy tắc không thể thương lượng (Non-Negotiable Rules)
- Target framework: `.NET Framework 4.8`.
- Phiên bản ngôn ngữ C#: chỉ sử dụng `7.3`.
- Không thêm các thư viện NuGet bên ngoài trừ khi được yêu cầu rõ ràng.
- Giữ nguyên kiến trúc và cách chia tách file hiện tại của dự án.
- Không làm hỏng hoặc thay đổi luồng hoạt động của các Tab hiện tại: `Direct` và `Reverse`.
- Luôn luôn lưu và viết code bằng định dạng encoding UTF-8 (ưu tiên UTF-8 với BOM cho các file .NET/C#) để đảm bảo các trình biên tập và AI Agent (Claude, Visual Studio Code, Antigravity, Codex...) hiển thị/xử lý đúng tiếng Việt có dấu trong comment hoặc chuỗi ký tự mà không bị lỗi chính tả hay lỗi font.
- Đối với giao diện tối (Dark Theme), tất cả các màu sắc bổ sung hoặc tùy chỉnh (như màu nền nút bấm, màu hover, màu văn bản) phải được lựa chọn theo các tông màu tối hài hòa (ví dụ: bảng màu tối của Material Design), tránh sử dụng các màu quá sáng/neon gây chói mắt và phải bảo đảm độ tương phản rõ ràng so với màu nền xung quanh.


## Quy tắc hành vi khi ứng dụng chạy (Runtime Behavior Rules)
- Cửa sổ `Check mklink` bắt buộc phải là cửa sổ dạng non-modal (không chặn tương tác cửa sổ chính) và liên kết dữ liệu trực tiếp với `MainWindow`.
- Mọi thao tác có tính chất xóa/phá hủy dữ liệu phải hiển thị rõ ràng và minh bạch trong luồng cmd được tạo ra.
- Việc thực thi CMD phải được hiển thị rõ ràng để phục vụ việc debug (hiển thị từng giai đoạn và các dòng lệnh cụ thể).

## Tiêu chuẩn chất lượng đầu ra
- Quá trình Build phải kết thúc với:
  - `0 Cảnh báo (Warning)`
  - `0 Lỗi (Error)`
- Nếu chưa đạt chuẩn trên, tiếp tục sửa cho đến khi sạch lỗi trước khi báo cáo hoàn thành.

## Phong cách giao tiếp
- Ngắn gọn, súc tích và thực tế.
- Giải thích rõ những gì đã thay đổi và lý do tại sao.
- Báo cáo chính xác trạng thái build sau khi thay đổi.
