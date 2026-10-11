using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Orbital;
using Orbital.Data;
using Orbital.Hubs;
using Orbital.Models;
using Orbital.Service;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ── Kestrel request body limit ────────────────────────────────
// Default is ~28.6 MB. Raised to 33 MB to accommodate a 25 MB
// file + base64 inflation (~33%) being posted through the hub.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 33_000_000;
});

// 1. Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

// 2. Controllers & JSON Options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums (FriendshipStatus, CallType, CallStatus) serialize as strings in API responses
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// 3. Database Context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found in appsettings.json.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// 4. Identity Setup
builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequiredLength = 6;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// 5. Authentication & JWT Configuration
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.SaveToken = true;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),

            // Allow 2 minutes of clock drift between server and client.
            // Zero skew causes premature 401s when client clocks are
            // slightly behind. 2 min is the common baseline.
            ClockSkew = TimeSpan.FromMinutes(2),

            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };

        // CRITICAL FOR SIGNALR: Intercept token from query string on WebSocket upgrade
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    })
    .AddGoogle(googleOptions =>
    {
        googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
        googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
        googleOptions.SignInScheme = IdentityConstants.ExternalScheme;
    });

// 6. Application Services & In-Memory Presence Tracker
builder.Services.AddSingleton<EmailTemplateService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<FriendService>();
builder.Services.AddScoped<DirectMessageService>();
builder.Services.AddScoped<GroupService>();
builder.Services.AddScoped<CallService>();
builder.Services.AddScoped<UserService>();

builder.Services.AddSingleton<PresenceTracker>();

// 7. SignalR
// Raised MaximumReceiveMessageSize from the default of 32 KB to 30 MB
// so attachments (images, files, media) can be sent over the hub. The
// default limit silently kills the connection on any payload larger
// than 32 KB.
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 30 * 1024 * 1024; // 30 MB
});

// 8. CORS — permissive for dev, tighten before production
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Pipeline Configuration (Strict Order Matters)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Redirect HTTP → HTTPS. Must run before static files and endpoints.
app.UseHttpsRedirection();

// CORS must sit before Authentication & Authorization
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.UseStaticFiles();

app.MapControllers();

// Map SignalR Hub endpoint
app.MapHub<ChatHub>("/hubs/chat");

app.Run();