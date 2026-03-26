using System.Security.Claims;
using JwtAuthApi.DTOs;
using JwtAuthApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JwtAuthApi.Controllers;

/// <summary>
/// Controller xử lý tất cả các endpoint liên quan đến Authentication.
/// Route: /api/auth/*
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    // POST /api/auth/register
    /// <summary>Đăng ký tài khoản mới</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        // Validate input cơ bản
        if (string.IsNullOrWhiteSpace(dto.Username) ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Password))
        {
            return BadRequest(new { message = "Username, Email và Password là bắt buộc." });
        }

        if (dto.Password.Length < 8)
            return BadRequest(new { message = "Mật khẩu phải có ít nhất 8 ký tự." });

        var (success, message) = await _authService.RegisterAsync(dto);

        if (!success)
            return Conflict(new { message }); // 409 Conflict khi email/username trùng

        return Ok(new { message });
    }

    // POST /api/auth/login
    /// <summary>
    /// Đăng nhập và nhận Access Token + Refresh Token qua HttpOnly Cookie.
    /// Client không cần lưu hay gửi lại thủ công, trình duyệt sẽ tự động quản lý.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var result = await _authService.LoginAsync(dto);

        if (result == null)
            return Unauthorized(new { message = "Email hoặc mật khẩu không đúng." });

        SetTokenCookies(result.AccessToken, result.RefreshToken, result.AccessTokenExpiry);

        return Ok(new { message = "Đăng nhập thành công", username = result.Username, role = result.Role });
    }

    // POST /api/auth/refresh
    /// <summary>
    /// Lấy Access Token mới khi token cũ hết hạn (tự động qua Cookie).
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        // 1. Chỉ cần đọc RefreshToken từ Cookie để làm mới
        var refreshToken = Request.Cookies["RefreshToken"];

        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { message = "Thiếu Refresh Token." });

        // 2. AuthService sẽ kiểm tra logic trong DB và trả về cặp token mới
        var result = await _authService.RefreshTokenAsync(refreshToken);

        if (result == null)
            return Unauthorized(new { message = "Refresh token không hợp lệ hoặc đã hết hạn." });

        // 3. Ghi đè bộ Token mới vào Cookie
        SetTokenCookies(result.AccessToken, result.RefreshToken, result.AccessTokenExpiry);

        return Ok(new { message = "Làm mới token thành công" });
    }

    // POST /api/auth/logout
    /// <summary>
    /// Đăng xuất khỏi thiết bị hiện tại và xóa Cookie.
    /// Yêu cầu: phải đang đăng nhập.
    /// </summary>
    [HttpPost("logout")]
    [Authorize] // ← Chỉ user đã đăng nhập mới được gọi
    public async Task<IActionResult> Logout()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out int userId))
            return Unauthorized();

        var refreshToken = Request.Cookies["RefreshToken"];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            var success = await _authService.RevokeTokenAsync(refreshToken, userId);
            if (!success)
            {
                // Vẫn tiếp tục xóa cookie dù refresh token db không hợp lệ
            }
        }

        Response.Cookies.Delete("AccessToken");
        Response.Cookies.Delete("RefreshToken");

        return Ok(new { message = "Đăng xuất thành công." });
    }

    // POST /api/auth/logout-all
    /// <summary>
    /// Đăng xuất khỏi TẤT CẢ thiết bị.
    /// Thu hồi tất cả refresh token của user này và xóa Cookie.
    /// </summary>
    [HttpPost("logout-all")]
    [Authorize]
    public async Task<IActionResult> LogoutAll()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out int userId))
            return Unauthorized();

        await _authService.RevokeAllTokensAsync(userId);

        Response.Cookies.Delete("AccessToken");
        Response.Cookies.Delete("RefreshToken");

        return Ok(new { message = "Đã đăng xuất khỏi tất cả thiết bị." });
    }

    private void SetTokenCookies(string accessToken, string refreshToken, DateTime accessTokenExpiry)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // Cần HTTPS
            SameSite = SameSiteMode.Strict,
            Expires = accessTokenExpiry // Thường để AccessTokenExpiry. Refresh Token cookie có thể hết hạn sau. Ở đây làm đơn giản theo AccessToken.
        };

        Response.Cookies.Append("AccessToken", accessToken, cookieOptions);

        // Cookie Refresh Token có thể sống lâu hơn
        // Có thể inject IConfiguration để đọc ngày, tạm cấu hình dài (1 năm) hoặc cùng AccessToken.
        // Ở đây cấu hình đơn giản để test.
        var refreshCookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddDays(7) // Tương đương RefreshTokenExpiryDays
        };

        Response.Cookies.Append("RefreshToken", refreshToken, refreshCookieOptions);
    }
}
