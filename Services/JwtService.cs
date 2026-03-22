using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JwtAuthApi.Models;
using Microsoft.IdentityModel.Tokens;

namespace JwtAuthApi.Services;

/// <summary>
/// Service xử lý tất cả logic liên quan đến JWT Token.
/// Được inject vào AuthService thông qua DI (Dependency Injection).
/// </summary>
public class JwtService
{
    private readonly IConfiguration _configuration;

    // Constructor Injection: ASP.NET tự động inject IConfiguration
    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Tạo JWT Access Token cho user.
    /// Token này có thời hạn ngắn (15 phút).
    /// 
    /// Cấu trúc JWT gồm 3 phần: Header.Payload.Signature
    /// - Header: loại thuật toán (HS256)
    /// - Payload: các "claims" (thông tin) về user
    /// - Signature: chữ ký đảm bảo token không bị giả mạo
    /// </summary>
    public string GenerateAccessToken(User user)
    {
        // 1. Lấy key bí mật từ config
        var secretKey = _configuration["Jwt:SecretKey"]!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        // 2. Tạo chữ ký với thuật toán HMAC-SHA256
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // 3. Định nghĩa các "Claims" - thông tin nhúng vào token
        // Client có thể đọc claims này mà KHÔNG cần gọi server
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),  // ID user
            new Claim(ClaimTypes.Name, user.Username),                  // Tên user
            new Claim(ClaimTypes.Email, user.Email),                    // Email user
            new Claim(ClaimTypes.Role, user.Role),                      // Role (dùng cho Authorization)
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // JWT ID duy nhất
            new Claim(JwtRegisteredClaimNames.Iat,                      // Thời điểm tạo token
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        // 4. Tạo JWT token object
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],       // Ai phát hành token
            audience: _configuration["Jwt:Audience"],   // Token dành cho ai
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(
                double.Parse(_configuration["Jwt:AccessTokenExpiryMinutes"]!)), // Thời hạn
            signingCredentials: credentials
        );

        // 5. Chuyển token object thành chuỗi string (Header.Payload.Signature)
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Tạo Refresh Token - là một chuỗi ngẫu nhiên, an toàn về mặt cryptographic.
    /// Khác với Access Token, Refresh Token KHÔNG chứa thông tin user.
    /// Nó chỉ là "chìa khóa" để xin Access Token mới.
    /// </summary>
    public string GenerateRefreshToken()
    {
        // Tạo 64 bytes ngẫu nhiên → chuyển thành Base64 string
        // RandomNumberGenerator đảm bảo không thể đoán được
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }

    /// <summary>
    /// Đọc thông tin từ một Access Token đã HẾT HẠN.
    /// Dùng khi client muốn refresh - ta cần biết user nào đang refresh
    /// mà không cần validate thời hạn của token.
    /// </summary>
    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var secretKey = _configuration["Jwt:SecretKey"]!;

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = _configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidateLifetime = false // ← Quan trọng: BỎ QUA kiểm tra hết hạn
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            // Validate chữ ký nhưng bỏ qua hết hạn
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);

            // Kiểm tra đúng loại thuật toán (tránh bị tấn công algorithm confusion)
            if (securityToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }
}
