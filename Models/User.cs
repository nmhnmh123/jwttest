namespace JwtAuthApi.Models;

/// <summary>
/// Entity đại diện cho người dùng trong hệ thống.
/// Lưu trữ thông tin đăng nhập và danh sách refresh token.
/// </summary>
public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Mật khẩu đã được hash bằng BCrypt (KHÔNG lưu plain text).
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "User"; // "User" hoặc "Admin"

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Danh sách các Refresh Token của user này.
    /// Một user có thể đăng nhập từ nhiều thiết bị → nhiều token.
    /// </summary>
    public List<RefreshToken> RefreshTokens { get; set; } = new();
}
