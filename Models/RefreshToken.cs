namespace JwtAuthApi.Models;

/// <summary>
/// Entity đại diện cho một Refresh Token.
/// Mỗi refresh token được lưu vào database để kiểm soát và thu hồi.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }

    /// <summary>
    /// Chuỗi token ngẫu nhiên (được tạo bằng RandomNumberGenerator).
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Thời điểm hết hạn. Mặc định 7 ngày.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Thời điểm token được tạo ra.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Token đã được sử dụng chưa? (Chống replay attack)
    /// Một refresh token chỉ được dùng 1 lần.
    /// </summary>
    public bool IsUsed { get; set; } = false;

    /// <summary>
    /// Token đã bị thu hồi không? (khi user logout hoặc admin thu hồi)
    /// </summary>
    public bool IsRevoked { get; set; } = false;

    // Foreign Key - liên kết với User
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// Kiểm tra token có còn hợp lệ không:
    /// - Chưa hết hạn
    /// - Chưa được dùng
    /// - Chưa bị thu hồi
    /// </summary>
    public bool IsActive => !IsUsed && !IsRevoked && ExpiresAt > DateTime.UtcNow;
}
