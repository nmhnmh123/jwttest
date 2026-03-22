# 📘 Tài Liệu: JWT Authentication & Authorization trong .NET 9

> Tài liệu này giải thích **chi tiết từng bước** cách code JWT Authentication + Refresh Token hoạt động trong project này. Dành cho người mới bắt đầu.

---

## 📁 Cấu Trúc Project

```
JwtAuthApi/
├── Models/
│   ├── User.cs              # Entity User (bảng trong database)
│   └── RefreshToken.cs      # Entity RefreshToken (bảng trong database)
│
├── DTOs/
│   └── AuthDtos.cs          # Data Transfer Objects (dữ liệu vào/ra API)
│
├── Data/
│   └── ApplicationDbContext.cs  # Kết nối database qua Entity Framework
│
├── Services/
│   ├── JwtService.cs        # Tạo và validate JWT Token
│   └── AuthService.cs       # Business logic: đăng ký, đăng nhập, refresh
│
├── Controllers/
│   ├── AuthController.cs    # API endpoints: /register, /login, /refresh, /logout
│   └── UsersController.cs   # API endpoints được bảo vệ bằng JWT
│
├── Program.cs               # Cấu hình ứng dụng (DI, JWT, EF, Swagger)
└── appsettings.json         # Cấu hình: JWT key, connection string
```

---

## 🧠 Kiến Thức Nền Tảng

### JWT (JSON Web Token) Là Gì?

JWT là một **chuỗi token dạng văn bản** được dùng để xác thực người dùng. Thay vì lưu session trên server, JWT cho phép **server stateless** - mọi thông tin cần thiết đều nằm trong token.

**Cấu trúc JWT gồm 3 phần, ngăn cách bởi dấu chấm (`.`):**

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9    ← Header (Base64)
.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIn0  ← Payload/Claims (Base64)
.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c           ← Signature (HMAC-SHA256)
```

- **Header**: Loại thuật toán ký (`HS256`)
- **Payload**: Thông tin user (userId, email, role...) - **ai cũng có thể đọc được**
- **Signature**: Chữ ký bảo đảm token không bị sửa đổi - **chỉ server mới tạo được**

> ⚠️ **Quan trọng**: Payload có thể decode được bởi bất kỳ ai, **đừng bao giờ đặt thông tin nhạy cảm** (mật khẩu, số thẻ...) vào JWT!

### Tại Sao Cần Refresh Token?

| | Access Token | Refresh Token |
|---|---|---|
| **Mục đích** | Truy cập API | Lấy Access Token mới |
| **Thời hạn** | Ngắn (15 phút) | Dài (7 ngày) |
| **Lưu ở đâu** | Memory / localStorage | HttpOnly Cookie hoặc Secure Storage |
| **Lưu trong DB** | ❌ Không | ✅ Có (để có thể thu hồi) |
| **Nội dung** | Chứa thông tin user | Chỉ là chuỗi ngẫu nhiên |

**Lý do**: Nếu Access Token bị đánh cắp, thiệt hại chỉ kéo dài 15 phút. Refresh Token được lưu DB nên có thể **thu hồi ngay lập tức** khi phát hiện bị đánh cắp.

---

## 🔄 Luồng Hoạt Động

### Luồng 1: Đăng Ký (Register)

```
Client                          Server                        Database
  │                               │                               │
  │──POST /api/auth/register──────▶                               │
  │   { username, email, pass }   │                               │
  │                               │── Kiểm tra email trùng ──────▶│
  │                               │◀──────────────────────────────│
  │                               │── Hash password (BCrypt) ─────│
  │                               │── INSERT User ────────────────▶│
  │                               │◀──────────────────────────────│
  │◀──200 OK ─────────────────────│
  │   { "Đăng ký thành công" }    │
```

**Code tương ứng** (`AuthService.cs - RegisterAsync`):
1. `AnyAsync(u => u.Email == dto.Email)` → kiểm tra email trùng
2. `BCrypt.HashPassword(dto.Password, workFactor: 12)` → hash mật khẩu
3. `_db.Users.Add(user)` → lưu database

---

### Luồng 2: Đăng Nhập (Login)

```
Client                          Server                        Database
  │                               │                               │
  │──POST /api/auth/login─────────▶                               │
  │   { email, password }         │                               │
  │                               │── Tìm user theo email ────────▶│
  │                               │◀──────────────────────────────│
  │                               │── BCrypt.Verify(pass, hash) ──│
  │                               │                               │
  │                               │── Tạo Access Token (JWT)      │
  │                               │── Tạo Refresh Token (random)  │
  │                               │── INSERT RefreshToken ─────────▶│
  │                               │◀──────────────────────────────│
  │◀──200 OK ─────────────────────│
  │   Set-Cookie: AccessToken=...; HttpOnly; Secure; SameSite=Strict
  │   Set-Cookie: RefreshToken=...; HttpOnly; Secure; SameSite=Strict
  │   { "message", "username", "role" }
```

**Code tương ứng** (`AuthService.cs - LoginAsync` chuyển sang `AuthController.cs` set cookie):
```csharp
// 1. Tìm user
var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

// 2. Kiểm tra mật khẩu
if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
    return null;

// 3. Tạo và lưu tokens
return await CreateTokensAsync(user);
```

---

### Luồng 3: Gọi API Được Bảo Vệ (Tự động gửi Cookie)

```
Client                          ASP.NET Middleware             Controller
  │                               │                               │
  │──GET /api/users/me────────────▶                               │
  │   Cookie: AccessToken=...     │                               │
  │                               │                               │
  │                    UseAuthentication Middleware                │
  │                               │── Kiểm tra Authorization Header│
  │                               │── Nếu không có, đọc từ Cookie ─│
  │                               │── Validate chữ ký ────────────│
  │                               │── Parse Claims ───────────────│
  │                               │── Gắn User vào HttpContext ────│
  │                               │                               │
  │                    UseAuthorization Middleware                 │
  │                               │── Kiểm tra [Authorize] ───────▶│
  │                               │                               │
  │                               │                │── Đọc userId từ Claims
  │                               │                │── Truy vấn DB
  │◀──200 OK ─────────────────────│◀───────────────│
  │   { thông tin profile }       │                │
```

**Code trong Controller** (`UsersController.cs`):
```csharp
[Authorize] // ← Middleware tự động kiểm tra JWT trước khi vào đây
public async Task<IActionResult> GetMyProfile()
{
    // User.FindFirst() đọc Claims đã được parse từ JWT
    var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    // ... truy vấn database
}
```

---

### Luồng 4: Refresh Token (Tự động qua Cookie)

```
Client                          Server                        Database
  │                               │                               │
  │ (Access Token hết hạn)        │                               │
  │                               │                               │
  │──POST /api/auth/refresh───────▶                               │
  │   Cookie: AccessToken=...     │                               │
  │   Cookie: RefreshToken=...    │                               │
  │                               │── Đọc JWT từ Cookie (bỏ qua hết hạn)│
  │                               │── Đọc RefreshToken từ Cookie ─│
  │                               │── Lấy userId từ Claims ───────│
  │                               │── Tìm RefreshToken trong DB ──▶│
  │                               │◀──────────────────────────────│
  │                               │── Kiểm tra IsActive ──────────│
  │                               │── Đánh dấu token cũ IsUsed=true▶│
  │                               │── Tạo cặp token MỚI ──────────│
  │                               │── Lưu RefreshToken mới ────────▶│
  │◀──200 OK ─────────────────────│◀──────────────────────────────│
  │   Set-Cookie: AccessToken=... (mới)                           │
  │   Set-Cookie: RefreshToken=... (mới)                          │
```

> 🔐 **Refresh Token Rotation**: Mỗi lần refresh, token cũ bị đánh dấu `IsUsed = true` và token mới được tạo. Nếu ai đó dùng token cũ → bị từ chối ngay.

---

### Luồng 5: Đăng Xuất (Logout) - Xóa Cookie

```
Client                          Server                        Database
  │                               │                               │
  │──POST /api/auth/logout────────▶                               │
  │   Cookie: AccessToken=...     │                               │
  │   Cookie: RefreshToken=...    │                               │
  │                               │── Middleware xác thực JWT ─────│
  │                               │── Lấy userId từ Claims ───────│
  │                               │── UPDATE RefreshToken.IsRevoked=true▶│
  │◀──200 OK ─────────────────────│◀──────────────────────────────│
  │   Set-Cookie: AccessToken=... (Expires=Past)                  │
  │   Set-Cookie: RefreshToken=... (Expires=Past)                 │
```

---

## 📂 Giải Thích Chi Tiết Từng File

### 1. `Models/User.cs` - Entity Người Dùng

```csharp
public class User
{
    public int Id { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }  // ← Hash, không phải plain text!
    public string Role { get; set; }           // "User" hoặc "Admin"
    public List<RefreshToken> RefreshTokens { get; set; } // ← Navigation property
}
```

`RefreshTokens` là **navigation property** của Entity Framework - khi Include trong query, EF sẽ tự JOIN bảng RefreshTokens.

---

### 2. `Models/RefreshToken.cs` - Entity Refresh Token

```csharp
public class RefreshToken
{
    public string Token { get; set; }    // Chuỗi ngẫu nhiên 64 bytes
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }     // Đã dùng rồi?
    public bool IsRevoked { get; set; }  // Bị thu hồi?
    public int UserId { get; set; }      // Foreign Key

    // Computed property - tổng hợp 3 điều kiện
    public bool IsActive => !IsUsed && !IsRevoked && ExpiresAt > DateTime.UtcNow;
}
```

`IsActive` là **computed property** - không lưu vào DB, mà tính toán mỗi lần đọc.

---

### 3. `Data/ApplicationDbContext.cs` - Database Context

```csharp
public class ApplicationDbContext : DbContext
{
    // DbSet = bảng trong database
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Cấu hình: Email unique, cascade delete, etc.
    }
}
```

Entity Framework sẽ **tự động** tạo SQL INSERT/SELECT/UPDATE/DELETE từ code C#.

---

### 4. `Services/JwtService.cs` - Tạo JWT Token

```
GenerateAccessToken(user)
├── 1. Đọc SecretKey từ appsettings
├── 2. Tạo SymmetricSecurityKey từ key
├── 3. Tạo Claims (NameIdentifier, Name, Email, Role, Jti, Iat)
├── 4. Tạo JwtSecurityToken với Issuer, Audience, Claims, Expiry
└── 5. Serialize thành string "Header.Payload.Signature"

GenerateRefreshToken()
└── RandomNumberGenerator.GetBytes(64) → Base64 string

GetPrincipalFromExpiredToken(token)
├── Validate chữ ký (ValidateIssuerSigningKey = true)
├── BỎ QUA hết hạn (ValidateLifetime = false) ← Quan trọng!
└── Trả về ClaimsPrincipal (danh sách claims)
```

---

### 5. `Services/AuthService.cs` - Business Logic

Đây là **trái tim** của hệ thống authentication. Xử lý:

| Method | Mô tả |
|---|---|
| `RegisterAsync` | Kiểm tra trùng → Hash pass → Lưu user |
| `LoginAsync` | Tìm user → Verify pass → Tạo tokens |
| `RefreshTokenAsync` | Validate → Đánh dấu cũ → Tạo mới |
| `RevokeTokenAsync` | Logout 1 thiết bị |
| `RevokeAllTokensAsync` | Logout tất cả thiết bị |
| `CreateTokensAsync` (private) | Tạo cặp token + lưu DB + dọn token cũ |

---

### 6. `Controllers/AuthController.cs` - API Endpoints

| Endpoint | Method | Auth? | Mô tả |
|---|---|---|---|
| `/api/auth/register` | POST | ❌ | Đăng ký |
| `/api/auth/login` | POST | ❌ | Đăng nhập (Set HttpOnly Cookies) |
| `/api/auth/refresh` | POST | ❌* | Refresh token (Đọc từ Cookies, Set Cookies mới) |
| `/api/auth/logout` | POST | ✅ | Logout 1 thiết bị (Xóa Cookies) |
| `/api/auth/logout-all` | POST | ✅ | Logout tất cả (Xóa Cookies) |

*`/refresh` cần Access Token (hết hạn) và Refresh Token được gửi tự động qua Cookie.

---

### 7. `Controllers/UsersController.cs` - Protected Endpoints

```csharp
[Authorize] // ← Class-level: tất cả endpoints đều cần JWT
public class UsersController : ControllerBase
{
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile() { ... }

    [HttpGet]
    [Authorize(Roles = "Admin")] // ← Method-level: chỉ Admin
    public async Task<IActionResult> GetAllUsers() { ... }
}
```

---

### 8. `Program.cs` - Cấu Hình Ứng Dụng

Thứ tự **cực kỳ quan trọng** trong Middleware Pipeline:

```csharp
// ✅ ĐÚNG - Authentication PHẢI trước Authorization
app.UseAuthentication(); // Parse JWT → gắn User vào HttpContext
app.UseAuthorization();  // Kiểm tra User có quyền không

// ❌ SAI - Authorization trước sẽ không thấy User
app.UseAuthorization();
app.UseAuthentication();
```

---

## 🔒 Bảo Mật: BCrypt Password Hashing

```
Mật khẩu gốc: "MyPassword123"
         ↓
BCrypt.HashPassword("MyPassword123", workFactor: 12)
         ↓
Stored hash: "$2a$12$N9qo8uLOickgx2ZMRZo......" (60 ký tự)
```

**Tại sao BCrypt?**
- Tự động tạo "salt" ngẫu nhiên → 2 user cùng mật khẩu có hash khác nhau
- `workFactor=12` → 2^12 vòng lặp → ~250ms để hash → **brute force rất chậm**
- Không thể reverse (one-way function)

**Kiểm tra mật khẩu**:
```csharp
BCrypt.Verify("MyPassword123", storedHash) // → true/false
```

---

## 🚀 Chạy Thử Project

```bash
cd JwtAuthApi
dotnet run
```

Mở trình duyệt: `http://localhost:5XXX` → Swagger UI

**Thứ tự test (Sử dụng Postman/Trình duyệt thay vì Swagger để test Cookie chuẩn nhất):**
1. `POST /api/auth/register` với `{ "username": "test", "email": "test@test.com", "password": "password123" }`
2. `POST /api/auth/login` → kiểm tra response Headers để thấy `Set-Cookie` cho `AccessToken` và `RefreshToken`.
3. `GET /api/users/me` → không cần thêm Header `Authorization`, cookie sẽ tự động được gửi và trả về thông tin.
4. Chờ Access Token rớt (hoặc đổi expiry ngắn) → `POST /api/auth/refresh` (cookie tự gửi) → lấy Cookie mới.
5. `POST /api/auth/logout` → đăng xuất và xoá cookie.

*(Lưu ý: Swagger UI mặc định không hiển thị hoặc không tự đính kèm HttpOnly cookies tốt khi khác domain hoặc cấu hình SameSite. Nên dùng Postman hoặc 1 trang web frontend để test luồng cookie này).*

---

## ⚙️ Cấu Hình (`appsettings.json`)

```json
{
  "Jwt": {
    "SecretKey": "...",                    // ← THAY BẰNG KEY BÍ MẬT THẬT!
    "Issuer": "JwtAuthApi",               // Tên server phát hành token
    "Audience": "JwtAuthApiClients",      // Tên client nhận token
    "AccessTokenExpiryMinutes": "15",     // Access token hết hạn sau 15 phút
    "RefreshTokenExpiryDays": "7"         // Refresh token hết hạn sau 7 ngày
  }
}
```

> ⚠️ **Production**: Không commit `SecretKey` lên Git! Dùng biến môi trường hoặc Azure Key Vault.

---

## ❓ Câu Hỏi Thường Gặp

**Q: Tại sao Access Token chỉ 15 phút?**
> A: Nếu bị đánh cắp (XSS attack...), hacker chỉ có 15 phút để lạm dụng. Ngắn = an toàn hơn.

**Q: Tại sao phải lưu JWT trong HttpOnly Cookie thay vì `localStorage`?**
> A: Lưu token vào `localStorage` khiến token dễ bị đánh cắp nếu trang web dính lỗi XSS (hacker chèn mã JS độc hại). `HttpOnly Cookie` ngăn chặn JavaScript client đọc được nội dung token, giúp bảo vệ an toàn hơn trước các cuộc tấn công XSS.

**Q: Refresh Token Rotation là gì?**
> A: Mỗi lần dùng Refresh Token để refresh → token cũ bị đánh dấu `IsUsed = true` và token mới được tạo. Nếu ai đó dùng token cũ đã dùng rồi → phát hiện rò rỉ token.

**Q: Tại sao `GetPrincipalFromExpiredToken` không validate lifetime?**
> A: Khi client gọi `/refresh`, Access Token của họ **đã hết hạn** (đó là lý do họ cần refresh). Server cần đọc userId từ token đó để tìm user, nhưng không cần validate thời hạn vì đã kiểm tra Refresh Token thay thế. Chữ ký vẫn được validate để đảm bảo token không bị giả mạo.
