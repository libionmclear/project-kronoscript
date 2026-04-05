using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Services;

var builder = WebApplication.CreateBuilder(args);

// EF Core + PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
    // Lockout: lock account for 10 minutes after 5 failed attempts
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Cookie config
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
});

// Anti-forgery
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

// Email sender (SendGrid)
builder.Services.AddTransient<IEmailSender, EmailSender>();

// Application services
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<IFriendService, FriendService>();
builder.Services.AddScoped<IRelativeService, RelativeService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IDiffService, DiffService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Auto-migrate on startup and seed admin data
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        // Safety net: create tables that manual migrations may have missed
        var ensureTables = new[]
        {
            @"CREATE TABLE IF NOT EXISTS ""Messages"" (
                ""Id""              SERIAL PRIMARY KEY,
                ""SenderUserId""    TEXT NOT NULL,
                ""RecipientUserId"" TEXT NOT NULL,
                ""Body""            VARCHAR(2000) NOT NULL,
                ""SentAt""          TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""IsRead""          BOOLEAN NOT NULL DEFAULT FALSE
            )",
            @"CREATE TABLE IF NOT EXISTS ""Tips"" (
                ""Id""        SERIAL PRIMARY KEY,
                ""Type""      INTEGER NOT NULL DEFAULT 0,
                ""Text""      VARCHAR(500) NOT NULL,
                ""IsActive""  BOOLEAN NOT NULL DEFAULT TRUE,
                ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            )",
            @"CREATE TABLE IF NOT EXISTS ""UserBans"" (
                ""Id""             SERIAL PRIMARY KEY,
                ""UserId""         TEXT,
                ""BannedEmail""    VARCHAR(256) NOT NULL,
                ""BanType""        INTEGER NOT NULL,
                ""BannedAt""       TIMESTAMP WITH TIME ZONE NOT NULL,
                ""BanExpiry""      TIMESTAMP WITH TIME ZONE,
                ""BannedByUserId"" TEXT,
                ""Reason""         VARCHAR(500)
            )"
        };
        foreach (var sql in ensureTables)
        {
            try { await db.Database.ExecuteSqlRawAsync(sql); }
            catch (Exception ex2) { logger.LogWarning(ex2, "Could not ensure table exists (may already exist)."); }
        }

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Ensure Admin role exists
        if (!await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new IdentityRole("Admin"));

        // Seed admin user
        const string adminEmail = "marco.bellini@live.com";
        const string adminUserName = "kronoadmin";

        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new ApplicationUser
            {
                UserName = adminUserName,
                Email = adminEmail,
                DisplayName = adminUserName,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };
            await userManager.CreateAsync(adminUser, "Admin@123456");
        }

        if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
            await userManager.AddToRoleAsync(adminUser, "Admin");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database migration or seeding. The app will continue to start.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
