using JwtAuthApi.Models;
using Microsoft.EntityFrameworkCore;

namespace JwtAuthApi.Data;

/// <summary>
/// DbContext của ứng dụng - là cầu nối giữa code C# và database.
/// Entity Framework sẽ tự động tạo bảng dựa trên các DbSet.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Bảng Users trong database
    public DbSet<User> Users => Set<User>();

    // Bảng RefreshTokens trong database
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Cấu hình bảng User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique(); // Email không được trùng
            entity.HasIndex(u => u.Username).IsUnique(); // Username không được trùng
            entity.Property(u => u.Email).IsRequired().HasMaxLength(256);
            entity.Property(u => u.Username).IsRequired().HasMaxLength(100);
            entity.Property(u => u.PasswordHash).IsRequired();
        });

        // Cấu hình bảng RefreshToken
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(rt => rt.Id);
            entity.HasIndex(rt => rt.Token).IsUnique(); // Token không được trùng
            entity.Property(rt => rt.Token).IsRequired();

            // Mỗi RefreshToken thuộc về 1 User
            // 1 User có nhiều RefreshToken (đăng nhập nhiều thiết bị)
            entity.HasOne(rt => rt.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(rt => rt.UserId)
                  .OnDelete(DeleteBehavior.Cascade); // Xóa user → xóa cả refresh token
        });
    }
}
