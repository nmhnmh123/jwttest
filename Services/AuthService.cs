using System.Security.Claims;
using JwtAuthApi.Data;
using JwtAuthApi.DTOs;
using JwtAuthApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JwtAuthApi.Services;

/// <summary>
/// Service xử lý business logic về Authentication:
/// Đăng ký, Đăng nhập, Refresh Token, Logout.
/// </summary>
public class AuthService
{
    private readonly ApplicationDbContext _db;
    private readonly JwtService _jwtService;
    private readonly IConfiguration _configuration;

    public AuthService(ApplicationDbContext db, JwtService jwtService, IConfiguration configuration)
    {
        _db = db;
        _jwtService = jwtService;
        _configuration = configuration;
    }

    // =====================================================
    // ĐĂNG KÝ (Register)
    // =====================================================

    /// <summary>
    /// Luồng đăng ký:
    /// 1. Kiểm tra email/username đã tồn tại chưa
    /// 2. Hash mật khẩu bằng BCrypt
    /// 3. Lưu user vào database
    /// </summary>
    public async Task<(bool Success, string Message)> RegisterAsync(RegisterDto dto)
    {
        // 1. Kiểm tra email đã tồn tại chưa
        if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
            return (false, "Email đã được sử dụng.");

        // 2. Kiểm tra username đã tồn tại chưa
        if (await _db.Users.AnyAsync(u => u.Username == dto.Username))
            return (false, "Username đã được sử dụng.");

        // 3. Hash mật khẩu - BCrypt tự tạo "salt" ngẫu nhiên
        // WorkFactor=12 nghĩa là thuật toán lặp 2^12 lần → chậm cố ý để khó brute-force
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12);

        // 4. Tạo user mới
        var user = new User
        {
            Username = dto.Username,
            Email = dto.Email,
            PasswordHash = passwordHash,
            Role = "User" // User mới mặc định là "User", không phải "Admin"
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return (true, "Đăng ký thành công!");
    }

    // =====================================================
    // ĐĂNG NHẬP (Login)
    // =====================================================

    /// <summary>
    /// Luồng đăng nhập:
    /// 1. Tìm user theo email
    /// 2. Xác minh mật khẩu với BCrypt
    /// 3. Tạo Access Token + Refresh Token
    /// 4. Lưu Refresh Token vào database
    /// 5. Trả về cả 2 token cho client
    /// </summary>
    public async Task<TokenResponseDto?> LoginAsync(LoginDto dto)
    {
        // 1. Tìm user theo email (bao gồm cả RefreshTokens)
        var user = await _db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null) return null; // User không tồn tại

        // 2. Kiểm tra mật khẩu với BCrypt
        // BCrypt.Verify so sánh plain text với hash đã lưu
        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return null; // Sai mật khẩu

        // 3. Tạo token
        return await CreateTokensAsync(user);
    }

    // =====================================================
    // LÀM MỚI TOKEN (Refresh)
    // =====================================================

    /// <summary>
    /// Luồng refresh token:
    /// 1. Đọc thông tin user từ Access Token cũ (đã hết hạn)
    /// 2. Tìm Refresh Token trong database
    /// 3. Kiểm tra Refresh Token còn hợp lệ không
    /// 4. Đánh dấu Refresh Token cũ là ĐÃ DÙNG (không dùng lại được)
    /// 5. Tạo cặp token mới
    /// </summary>
    public async Task<TokenResponseDto?> RefreshTokenAsync(string refreshToken)
    {
        // 1. Tìm Refresh Token trong database và nạp luôn thông tin User liên quan
        var storedToken = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        // 2. Kiểm tra tính hợp lệ
        // - Token tồn tại
        // - Token còn hạn, chưa bị dùng, chưa bị thu hồi (logic IsActive trong entity)
        if (storedToken == null || !storedToken.IsActive)
            return null;

        // 3. Đánh dấu token cũ là ĐÃ DÙNG (Rotation pattern - bảo mật hơn)
        storedToken.IsUsed = true;
        _db.RefreshTokens.Update(storedToken);

        // 4. Tạo cặp token mới cho User của refresh token này
        return await CreateTokensAsync(storedToken.User);
    }

    // =====================================================
    // ĐĂNG XUẤT (Logout)
    // =====================================================

    /// <summary>
    /// Thu hồi một Refresh Token cụ thể (logout khỏi 1 thiết bị).
    /// </summary>
    public async Task<bool> RevokeTokenAsync(string refreshToken, int userId)
    {
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken && rt.UserId == userId);

        if (token == null || !token.IsActive)
            return false;

        token.IsRevoked = true;
        _db.RefreshTokens.Update(token);
        await _db.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Thu hồi TẤT CẢ Refresh Token của user (logout khỏi tất cả thiết bị).
    /// </summary>
    public async Task RevokeAllTokensAsync(int userId)
    {
        var activeTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync();

        foreach (var token in activeTokens)
            token.IsRevoked = true;

        await _db.SaveChangesAsync();
    }

    // =====================================================
    // PRIVATE HELPER
    // =====================================================

    /// <summary>
    /// Tạo Access Token + Refresh Token và lưu Refresh Token vào DB.
    /// </summary>
    private async Task<TokenResponseDto> CreateTokensAsync(User user)
    {
        // Tạo Access Token (JWT)
        var accessToken = _jwtService.GenerateAccessToken(user);

        // Tạo Refresh Token (chuỗi ngẫu nhiên)
        var refreshTokenString = _jwtService.GenerateRefreshToken();

        // Thời hạn refresh token (từ config)
        var refreshTokenExpiryDays = double.Parse(_configuration["Jwt:RefreshTokenExpiryDays"]!);

        // Lưu Refresh Token vào database
        var refreshTokenEntity = new RefreshToken
        {
            Token = refreshTokenString,
            ExpiresAt = DateTime.UtcNow.AddDays(refreshTokenExpiryDays),
            UserId = user.Id
        };

        _db.RefreshTokens.Add(refreshTokenEntity);

        // Dọn dẹp: xóa các token cũ đã hết hạn hoặc đã dùng của user này
        // Tránh database phình to theo thời gian
        var oldTokens = user.RefreshTokens
            .Where(rt => !rt.IsActive)
            .ToList();
        _db.RefreshTokens.RemoveRange(oldTokens);

        await _db.SaveChangesAsync();

        var accessTokenExpiry = DateTime.UtcNow.AddMinutes(
            double.Parse(_configuration["Jwt:AccessTokenExpiryMinutes"]!));

        return new TokenResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenString,
            AccessTokenExpiry = accessTokenExpiry,
            Username = user.Username,
            Role = user.Role
        };
    }
}
