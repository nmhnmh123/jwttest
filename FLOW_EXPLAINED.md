# Tóm Tắt Luồng Xác Thực: JWT + Cuộc Chiến Giữa Frontend & Backend

Tài liệu này giải thích bản chất thực sự của luồng xác thực (Authentication) và phân quyền (Authorization) trong dự án này, đặc biệt tập trung vào cách **AccessToken**, **RefreshToken** và **HttpOnly Cookie** phối hợp với nhau.

---

## 1. Các Khái Niệm Cốt Lõi (Được Ví Von)

- **Authentication (Xác Thực):** "Bạn là ai?". Giống như việc bạn đưa CMND cho lễ tân tòa nhà để chứng minh danh tính.
- **Authorization (Phân Quyền):** "Bạn được phép làm gì?". Sau khi vào tòa nhà, bạn được cầm chìa khóa vào phòng Nhân Sự, hay chỉ được đứng ở sảnh?
- **AccessToken:** "Thẻ Ra Vào Tòa Nhà" (Thời hạn ngắn: 15 phút). Dùng để đi qua các trạm gác (gọi API).
- **RefreshToken:** "Giấy Xác Nhận Cấp Lại Thẻ" (Thời hạn dài: 7 ngày). Lưu ở Database Backend. Dùng để xin cấp lại thẻ AccessToken mỗi khi nó hết hạn, mà không bắt người dùng phải nhập lại Mật Khẩu.

---

## 2. Tại Sao Lại Là HttpOnly Cookie?

Nhà thiết kế (Backend) đã cất 2 loại Thẻ (Tokens) này vào **HttpOnly Cookie** thay vì ném cho Frontend (React/Vue/Mobile) tự giữ trong `localStorage`.

### Tại sao Cookie này an toàn tuyệt đối?
1. **`HttpOnly = true`:** Frontend (code Javascript) bị "BỊT MẮT". Nó **tuyệt đối không thể đọc được nội dung Cookie này**. Kẻ trộm có hack được giao diện Frontend (lỗi XSS) cũng không thể lấy được JWT.
2. **`Secure = true`:** Cookie chỉ được giao nhận qua đường truyền HTTPS có mã hóa. Chống nghe lén mạng.
3. **`SameSite = Strict`:** Chống tấn công CSRF (Giả mạo yêu cầu từ trang web lừa đảo).

---

## 3. Luồng Hoạt Động (Flow) Thực Tế Của Ứng Dụng

Đây là cách hệ thống chạy trong đời thực, trả lời cho câu hỏi: *"Server tự refresh không? Frontend làm quen với Cookie thế nào?"*

### Bước 1: Xin Thẻ Lần Đầu (Login)
- Người dùng nhập Email + Mật khẩu trên React.
- React gọi API `POST /api/auth/login`.
- Server (Backend) kiểm tra đúng mật khẩu. Ký phát Token.
- Server ném lại 1 gói hàng chứa `Set-Cookie`.
- **Trình duyệt (Chrome, Safari...)** tự động hứng lấy 2 cái Cookie (Access + Refresh) và cất vào két sắt của trình duyệt. *React lúc này không làm gì, cũng không biết trong Cookie có gì.*

### Bước 2: Đi Qua Cửa Bảo Vệ (Gọi API)
- React muốn lấy thông tin cá nhân hiển thị lên màn hình. Nó gọi API `GET /api/users/me`.
- **Trình duyệt** lại đóng vai trò người giao hàng, tự động móc 2 cái Cookie từ két sắt đính kèm vào "Chuyến xe Request" gửi lên Server.
- Lễ tân tòa nhà (Middleware ASP.NET) moi Cookie ra kiểm tra. Thẻ `AccessToken` xịn, còn hạn? Cho vào! Server trả data về.

### Bước 3: Sự Cố - Thẻ Trắng Mực (Token Hết Hạn)
- 15 phút sau, thời hạn Cookie `AccessToken` đi đến hồi kết.
- **Trình duyệt tự động ném Cookie đó vào thùng rác**.
- React vẫn ngây thơ gọi API `GET /api/users/me`.
- Chuyến xe Request này chạy lên Server trong tình trạng **tay không** (mất Cookie AccessToken).
- Lễ tân tòa nhà dơ bảng cấm: **`Lỗi 401 Unauthorized`**.

### Bước 4: Hắc Kỷ Lục (Tự Động Làm Mới - Refresh Token)
- React (thường dùng `Axios Interceptor`) bắt được cái bảng cấm `401`.
- Phản ứng của React: *"À, thẻ chết rồi, gọi sếp cấp thẻ mới!"*
- React liền gửi một cú điện thoại khẩn (API `POST /api/auth/refresh`).
- Trong chuyến xe khẩn này, **Trình duyệt** tự động cắn rứt mang nốt cái `RefreshToken Cookie` (cái thẻ 7 ngày vẫn còn sống) ném lên Server.
- Server đối chiếu `RefreshToken` với Database: Khớp! Không bị ăn trộm!
- Server gửi lại 2 cái Cookie `Set-Cookie` mới toanh.
- Két sắt trình duyệt được cập nhật thẻ mới.

### Bước 5: Thử Lại Lần Nữa (Retry)
- Sau khi lấy được Cookie mới, React không báo lỗi gì ra màn hình điện thoại người dùng.
- Nó tự động ngầm lấy lại cái Request `GET /api/users/me` ban đầu, và thực hiện lại chuyến đi một lần nữa.
- Lần này trót lọt, data trả về. Người dùng vẫn tiếp tục lướt App mà không hề hay biết đằng sau một cuộc chiến xin thẻ vừa diễn ra gay cấn ở phần mềm!

---

## 4. Tóm Lược Về Quy Luật Chặn / Mở

- **Frontend gửi Request Lên Server:** Mọi máy tính trên trái đất đều có quyền gọi vào URL của cổng `/api/users/me`.
- **Server Chặn (Middleware):** Quy luật cực kỳ đơn giản. Không có thẻ (Cookie chứa JWT) hợp lệ, chưa hết hạn, ký đúng chữ ký của toà nhà -> Lập tức chặn lại và trả `401 Unauthorized`.
- **Client Bó Tay / Tuân Lệnh:** Frontend React không thể "Hack" hay "Giả mạo" JWT được. Nó phải ngoan ngoãn chạy theo luồng: *"Xin thẻ -> Xài thẻ -> Server báo 401 -> Đi cửa phụ xin thẻ mới -> Xài tiếp thẻ mới"*.
