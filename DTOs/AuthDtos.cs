namespace JwtAuthApi.DTOs;

// DTO nhận dữ liệu khi user đăng ký
public class RegisterDto
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

// DTO nhận dữ liệu khi user đăng nhập
public class LoginDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

// DTO trả về khi login thành công  
public class TokenResponseDto
{
    /// <summary>JWT Access Token (ngắn hạn, mặc định 15 phút)</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Refresh Token (dài hạn, mặc định 7 ngày)</summary>
    public string RefreshToken { get; set; } = string.Empty;

    public DateTime AccessTokenExpiry { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

// DTO nhận Refresh Token để gia hạn
public class RefreshTokenDto
{
    public string RefreshToken { get; set; } = string.Empty;
}
