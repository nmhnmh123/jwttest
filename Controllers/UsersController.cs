using System.Security.Claims;
using JwtAuthApi.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JwtAuthApi.Controllers;

/// <summary>
/// Controller minh họa cách bảo vệ endpoint bằng JWT Authorization.
/// Route: /api/users/*
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize] // ← Tất cả endpoint trong controller này đều yêu cầu đăng nhập
public class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public UsersController(ApplicationDbContext db)
    {
        _db = db;
    }

    // GET /api/users/me
    /// <summary>
    /// Lấy thông tin của chính mình.
    /// Yêu cầu: Access Token hợp lệ (bất kỳ role nào).
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        // User.FindFirst() truy xuất claims từ JWT đã được middleware parse
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var user = await _db.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.Role,
                u.CreatedAt
            })
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null) return NotFound();

        return Ok(user);
    }

    // GET /api/users
    /// <summary>
    /// Lấy danh sách tất cả users.
    /// Yêu cầu: Role "Admin" (Authorization dựa theo role).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")] // ← Chỉ Admin mới được xem danh sách user
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _db.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.Role,
                u.CreatedAt,
                ActiveTokenCount = u.RefreshTokens.Count(rt => !rt.IsUsed && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow)
            })
            .ToListAsync();

        return Ok(users);
    }
}
