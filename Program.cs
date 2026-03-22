using System.Text;
using JwtAuthApi.Data;
using JwtAuthApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// 1. CẤU HÌNH SERVICES (Dependency Injection Container)
// ============================================================

builder.Services.AddControllers();

// --- Swagger/OpenAPI ---
// Thêm nút "Authorize" vào Swagger UI để test JWT dễ hơn
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "JWT Auth API",
        Version = "v1",
        Description = "Demo JWT Authentication & Authorization với .NET 9"
    });

    // Thêm định nghĩa Bearer Token cho Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập JWT token. Ví dụ: eyJhbGci..."
    });

    // Áp dụng cho tất cả endpoint
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// --- Database (Entity Framework + SQLite) ---
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// --- Đăng ký Services vào DI Container ---
// AddScoped: tạo instance mới cho mỗi HTTP request
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<AuthService>();

// --- JWT Authentication ---
// Cấu hình cách ASP.NET xác thực JWT từ header "Authorization: Bearer <token>"
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]!;

builder.Services.AddAuthentication(options =>
{
    // Scheme mặc định cho Authentication và Challenge
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Quy tắc validate JWT khi nhận được request
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,                  // Kiểm tra "issuer" trong token
        ValidIssuer = builder.Configuration["Jwt:Issuer"],

        ValidateAudience = true,                 // Kiểm tra "audience" trong token
        ValidAudience = builder.Configuration["Jwt:Audience"],

        ValidateIssuerSigningKey = true,         // Kiểm tra chữ ký
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSecretKey)),

        ValidateLifetime = true,                 // Kiểm tra hạn sử dụng
        ClockSkew = TimeSpan.Zero               // Không cho phép lệch giờ (strict)
    };

    // --- Đọc token từ Cookie ---
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // Kiểm tra Header trước, nếu không có lấy từ Cookie
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                context.Token = authHeader.Substring("Bearer ".Length).Trim();
                return Task.CompletedTask;
            }

            var accessToken = context.Request.Cookies["AccessToken"];
            if (!string.IsNullOrEmpty(accessToken))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

// --- Authorization ---
builder.Services.AddAuthorization();

// ============================================================
// 2. XÂY DỰNG APPLICATION
// ============================================================

var app = builder.Build();

// --- Swagger UI (chỉ hiển thị trong môi trường Development) ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "JWT Auth API v1");
        c.RoutePrefix = string.Empty; // Swagger ở root URL: http://localhost:xxxx/
    });
}

app.UseHttpsRedirection();

// --- Middleware Pipeline (thứ tự RẤT QUAN TRỌNG) ---
// Authentication phải trước Authorization
app.UseAuthentication(); // ← Đọc JWT từ header, parse Claims
app.UseAuthorization();  // ← Kiểm tra Claims có đủ quyền không

app.MapControllers();

// ============================================================
// 3. TẠO DATABASE KHI CHẠY LẦN ĐẦU
// ============================================================
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    // Tạo database và apply tất cả migrations
    db.Database.EnsureCreated();
}

app.Run();
