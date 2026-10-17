using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
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

// ── 0. Data Protection — PERSIST KEYS ──────────────────────────────────────
var keysDir = Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keysDir);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("Orbital");

// ── 0b. Forwarded Headers — behind MonsterASP.NET reverse proxy ────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── 1. Kestrel & Multipart Form Limits (33 MB — file uploads) ──────────────
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 33_000_000;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = 33_000_000;
    options.MultipartBodyLengthLimit = 33_000_000;
    options.MultipartHeadersLengthLimit = 33_000_000;
});

// ── 2. Swagger / OpenAPI Configuration ─────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

// ── 3. Controllers & JSON Serialization ────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// ── 4. Database Context ────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found in appsettings.json.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ── 5. ASP.NET Core Identity ───────────────────────────────────────────────
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

// ── 6. Authentication & JWT Configuration ──────────────────────────────────
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

            ClockSkew = TimeSpan.FromMinutes(2),

            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };

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
    .AddScheme<GoogleOptions, CustomGoogleHandler>("Google", null);

// ── 6b. Provide the post-configure defaults that .AddGoogle() would have ───
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<
        IPostConfigureOptions<GoogleOptions>,
        OAuthPostConfigureOptions<GoogleOptions, GoogleHandler>>());

builder.Services.Configure<GoogleOptions>("Google", googleOptions =>
{
    googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
    googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
    googleOptions.SignInScheme = IdentityConstants.ExternalScheme;

    // Google's endpoints — OAuthPostConfigureOptions does not set these,
    // they're normally set by the internal GooglePostConfigureOptions.
    // Without them, Options.UserInformationEndpoint is null and
    // CustomGoogleHandler.CreateTicketAsync throws at runtime.
    googleOptions.AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    googleOptions.TokenEndpoint = "https://oauth2.googleapis.com/token";
    googleOptions.UserInformationEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
});

// ── 7. SignalR & Custom User ID Provider ───────────────────────────────────
builder.Services.AddSingleton<IUserIdProvider, CustomUserIdProvider>();

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 5 * 1024 * 1024; // 5 MB (was 33 MB)
    options.EnableDetailedErrors = true;
});

// ── 8. Application Services & In-Memory Presence Tracker ───────────────────
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
builder.Services.AddHostedService<StaleCallCleanupService>();

// ── 9. CORS Policy ─────────────────────────────────────────────────────────
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

// FIX: MUST be the very first middleware.
app.UseForwardedHeaders();

// ── 10. Swagger ────────────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Orbital API v1");
    c.RoutePrefix = "swagger";
});

// ── 11. Static File Serving ────────────────────────────────────────────────
var avatarsDir = Path.Combine(app.Environment.ContentRootPath, "avatars");
if (!Directory.Exists(avatarsDir))
{
    Directory.CreateDirectory(avatarsDir);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(avatarsDir),
    RequestPath = "/avatars"
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(app.Environment.ContentRootPath),
    RequestPath = ""
});

// ── 12. Middleware Pipeline Sequence ───────────────────────────────────────
app.UseHttpsRedirection();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");
app.MapGet("/health", () => Results.Ok(new { status = "ok", t = DateTime.UtcNow }))
   .AllowAnonymous();

app.Run();

// ── 13. SignalR Identity Resolution Helper ─────────────────────────────────
public class CustomUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? connection.User?.FindFirst("sub")?.Value;
    }
}