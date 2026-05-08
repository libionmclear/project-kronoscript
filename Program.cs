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
    options.SignIn.RequireConfirmedEmail = true;
    // Lockout: 5 attempts, then 5 min on the first lockout. AccountController
    // overrides LockoutEnd to 30 min on the second consecutive lockout based
    // on ApplicationUser.RecentLockoutCount.
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
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
builder.Services.AddHttpClient<ITranslationService, AzureTranslationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAccountDeletionService, AccountDeletionService>();
builder.Services.AddSingleton<IFileStorageService, AzureBlobFileStorageService>();
builder.Services.AddSingleton<IImageProcessor, ImageProcessor>();
builder.Services.AddScoped<IBadgeService, BadgeService>();

// Application Insights — auto-instruments requests, exceptions, dependencies.
// No-ops cleanly when APPLICATIONINSIGHTS_CONNECTION_STRING (or the
// ApplicationInsights:ConnectionString config key) is not set, so local dev
// stays quiet. Set the connection string in Azure App Service env vars.
builder.Services.AddApplicationInsightsTelemetry();

// Rate limiting — protect cost-sensitive (Translator) and abuse-prone (signup,
// password reset, login) endpoints. Per-user when authenticated; per-IP for anon.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static string KeyFor(HttpContext ctx) =>
        ctx.User?.Identity?.IsAuthenticated == true
            ? $"u:{ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value}"
            : $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    // Translator: cost-sensitive (each uncached call hits Azure Translator).
    options.AddPolicy("translate", ctx =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: KeyFor(ctx),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));

    // Anon write endpoints: signup + password reset request.
    options.AddPolicy("anon-write", ctx =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: KeyFor(ctx),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }));

    // Login attempts (Identity also locks the account; this catches IP-level spraying).
    options.AddPolicy("login", ctx =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: KeyFor(ctx),
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromHours(2);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

// Allow up to 250 MB request bodies (5 photos + a phone video easily exceed
// the 30 MB Kestrel default and would 413 silently on Quick Story uploads).
const long MaxUploadBytes = 250L * 1024 * 1024;
builder.WebHost.ConfigureKestrel(opts => opts.Limits.MaxRequestBodySize = MaxUploadBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = MaxUploadBytes;
    o.ValueLengthLimit = int.MaxValue;
    o.MultipartHeadersLengthLimit = int.MaxValue;
});

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
            )",
            @"CREATE TABLE IF NOT EXISTS ""MediaComments"" (
                ""Id""           SERIAL PRIMARY KEY,
                ""PostMediaId""  INTEGER NOT NULL,
                ""AuthorUserId"" TEXT NOT NULL,
                ""Body""         VARCHAR(1000) NOT NULL,
                ""CreatedAt""    TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            )",
            @"CREATE TABLE IF NOT EXISTS ""QuillMessages"" (
                ""Id""        SERIAL PRIMARY KEY,
                ""Text""      VARCHAR(500) NOT NULL,
                ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
                ""IsActive""  BOOLEAN NOT NULL DEFAULT TRUE,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            )",
            @"CREATE TABLE IF NOT EXISTS ""MemoryPrompts"" (
                ""Id""        SERIAL PRIMARY KEY,
                ""Text""      VARCHAR(500) NOT NULL,
                ""SortOrder"" INTEGER NOT NULL DEFAULT 0,
                ""IsActive""  BOOLEAN NOT NULL DEFAULT TRUE,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            )",
            @"CREATE TABLE IF NOT EXISTS ""PostTranslations"" (
                ""Id""                   SERIAL PRIMARY KEY,
                ""PostId""               INTEGER NOT NULL,
                ""LanguageCode""         VARCHAR(16) NOT NULL,
                ""DetectedFromLanguage"" VARCHAR(16),
                ""TitleTranslated""      VARCHAR(500),
                ""BodyTranslated""       TEXT NOT NULL,
                ""CreatedAt""            TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                CONSTRAINT ""UQ_PostTranslations_Post_Lang"" UNIQUE(""PostId"", ""LanguageCode"")
            )",
            @"CREATE TABLE IF NOT EXISTS ""CommentTranslations"" (
                ""Id""                   SERIAL PRIMARY KEY,
                ""CommentId""            INTEGER NOT NULL,
                ""LanguageCode""         VARCHAR(16) NOT NULL,
                ""DetectedFromLanguage"" VARCHAR(16),
                ""BodyTranslated""       TEXT NOT NULL,
                ""CreatedAt""            TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                CONSTRAINT ""UQ_CommentTranslations_Comment_Lang"" UNIQUE(""CommentId"", ""LanguageCode"")
            )",
            @"CREATE TABLE IF NOT EXISTS ""CommentLikes"" (
                ""Id""        SERIAL PRIMARY KEY,
                ""CommentId"" INTEGER NOT NULL,
                ""UserId""    TEXT NOT NULL,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                CONSTRAINT ""UQ_CommentLikes_Comment_User"" UNIQUE(""CommentId"", ""UserId"")
            )",
            @"CREATE TABLE IF NOT EXISTS ""Notifications"" (
                ""Id""          SERIAL PRIMARY KEY,
                ""UserId""      TEXT NOT NULL,
                ""Type""        INTEGER NOT NULL,
                ""Text""        VARCHAR(500) NOT NULL,
                ""LinkUrl""     VARCHAR(500),
                ""ActorUserId"" TEXT,
                ""CreatedAt""   TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""ReadAt""      TIMESTAMP WITH TIME ZONE
            )",
            @"CREATE INDEX IF NOT EXISTS ""IX_Notifications_User_Created"" ON ""Notifications"" (""UserId"", ""CreatedAt"" DESC)",
            @"CREATE TABLE IF NOT EXISTS ""UserBlocks"" (
                ""Id""             SERIAL PRIMARY KEY,
                ""BlockerUserId""  TEXT NOT NULL,
                ""BlockedUserId""  TEXT NOT NULL,
                ""CreatedAt""      TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                CONSTRAINT ""UQ_UserBlocks_Pair"" UNIQUE(""BlockerUserId"", ""BlockedUserId"")
            )",
            @"CREATE TABLE IF NOT EXISTS ""Reports"" (
                ""Id""              SERIAL PRIMARY KEY,
                ""ReporterUserId""  TEXT NOT NULL,
                ""TargetType""      INTEGER NOT NULL,
                ""TargetId""        VARCHAR(64) NOT NULL,
                ""Reason""          VARCHAR(1000),
                ""Status""          INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt""       TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""HandledAt""       TIMESTAMP WITH TIME ZONE,
                ""HandledByUserId"" TEXT
            )",
            @"CREATE INDEX IF NOT EXISTS ""IX_Reports_Status_Created"" ON ""Reports"" (""Status"", ""CreatedAt"" DESC)",
            @"CREATE TABLE IF NOT EXISTS ""Channels"" (
                ""Id""                SERIAL PRIMARY KEY,
                ""Name""              VARCHAR(80) NOT NULL,
                ""Slug""              VARCHAR(80) NOT NULL UNIQUE,
                ""Description""       VARCHAR(500),
                ""IconEmoji""         VARCHAR(8),
                ""AdminUserId""       TEXT,
                ""CreatedAt""         TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""CreatedByUserId""   TEXT NOT NULL
            )",
            @"CREATE TABLE IF NOT EXISTS ""WorkingIndexEntries"" (
                ""Id""           SERIAL PRIMARY KEY,
                ""OwnerUserId""  TEXT NOT NULL,
                ""Year""         INTEGER NOT NULL,
                ""MainEvent""    VARCHAR(500),
                ""Residence""    VARCHAR(300),
                ""SchoolJob""    VARCHAR(300),
                ""Relationship"" VARCHAR(300),
                ""Family""       VARCHAR(300),
                ""Vacation""     VARCHAR(300),
                ""Other""        VARCHAR(500),
                ""Notes""        VARCHAR(2000),
                ""Mood""         INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt""    TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
                ""UpdatedAt""    TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            )"
        };
        foreach (var sql in ensureTables)
        {
            try { await db.Database.ExecuteSqlRawAsync(sql); }
            catch (Exception ex2) { logger.LogWarning(ex2, "Could not ensure table exists (may already exist)."); }
        }

        // Safety net: add new columns that post-initial migrations may add
        var ensureColumns = new[]
        {
            @"ALTER TABLE ""Comments"" ADD COLUMN IF NOT EXISTS ""ParentCommentId"" INTEGER",
            @"ALTER TABLE ""PostLikes"" ADD COLUMN IF NOT EXISTS ""ReactionType"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""ProfileCardBackgroundUrl"" VARCHAR(500)",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""ShowOnlineStatus"" BOOLEAN NOT NULL DEFAULT TRUE",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""Nationalities"" VARCHAR(200)",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""BirthDateVisibility"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""GenderVisibility"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""BirthPlaceVisibility"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""CurrentLocationVisibility"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""NationalitiesVisibility"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""PreferredReadingLanguage"" VARCHAR(16)",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""RecentLockoutCount"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""LastSeenAt"" TIMESTAMP WITH TIME ZONE",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""AgreedToTermsAt"" TIMESTAMP WITH TIME ZONE",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""IsCompletelyPrivate"" BOOLEAN NOT NULL DEFAULT FALSE",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""SuspendedUntil"" TIMESTAMP WITH TIME ZONE",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""DeletionCodeHash"" VARCHAR(128)",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""DeletionCodeExpiresAt"" TIMESTAMP WITH TIME ZONE",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""AccountDeletionRequestedAt"" TIMESTAMP WITH TIME ZONE",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""LoginDaysCount"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""LastBadgeLevelPosts"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""LastBadgeLevelWords"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""LastBadgeLevelConnections"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""LastBadgeLevelComments"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""LastBadgeLevelLogins"" INTEGER NOT NULL DEFAULT 0",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""FoundingBadgeAcknowledged"" BOOLEAN NOT NULL DEFAULT FALSE",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""PreferredUiLanguage"" VARCHAR(16)",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""ManagedByUserId"" VARCHAR(450)",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""IsBiographical"" BOOLEAN NOT NULL DEFAULT FALSE",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""BiographicalEra"" VARCHAR(60)",
            @"ALTER TABLE ""AspNetUsers"" ADD COLUMN IF NOT EXISTS ""BiographicalSummary"" VARCHAR(500)",
            @"CREATE INDEX IF NOT EXISTS ""IX_AspNetUsers_ManagedByUserId"" ON ""AspNetUsers"" (""ManagedByUserId"")",
            // First-time switch to RequireConfirmedEmail: existing accounts predate the
            // confirmation flow, so retroactively mark them confirmed to avoid lockouts.
            @"UPDATE ""AspNetUsers"" SET ""EmailConfirmed"" = TRUE WHERE ""EmailConfirmed"" IS NOT TRUE AND ""CreatedAt"" < NOW() - INTERVAL '1 hour'",
            @"ALTER TABLE ""LifeEventPosts"" ADD COLUMN IF NOT EXISTS ""DeletedAt"" TIMESTAMP WITH TIME ZONE",
            @"ALTER TABLE ""LifeEventPosts"" ADD COLUMN IF NOT EXISTS ""ChannelId"" INTEGER",
            @"ALTER TABLE ""LifeEventPosts"" ADD COLUMN IF NOT EXISTS ""MusicUrl"" VARCHAR(500)",
            @"ALTER TABLE ""LifeEventPosts"" ADD COLUMN IF NOT EXISTS ""IsDraft"" BOOLEAN NOT NULL DEFAULT FALSE"
        };
        foreach (var sql in ensureColumns)
        {
            try { await db.Database.ExecuteSqlRawAsync(sql); }
            catch (Exception ex2) { logger.LogWarning(ex2, "Could not ensure column exists (may already exist)."); }
        }

        // Seed default quill messages on first run
        try
        {
            if (!await db.QuillMessages.AnyAsync())
            {
                var defaults = new[]
                {
                    "A memory at a time. A life over years. A book in the end.",
                    "Capture it. Share it. Live with it. Print it.",
                    "Don\u2019t write your story \u2014 catch it as it happens.",
                    "The only social network where the past matters as much as the present.",
                    "Your life, written by everyone who lived it with you.",
                    "Memories find you. We help you keep them.",
                    "Every life is an epic. We help you tell yours, slowly.",
                    "Share what\u2019s happening. Share what stayed with you.",
                    "Two voices, one memory, both true.",
                    "Because our loved ones remember what we have forgotten.",
                    "Tag the people who were there. Let them write the rest.",
                    "A place for shared memories \u2014 today\u2019s and yesterday\u2019s.",
                    "Organize friends by acquaintances, friends, and family \u2014 or write in private.",
                    "Privacy proof. Free. Ad-free.",
                    "Easily reorder your years. Then export the whole thing as a book.",
                    "When you\u2019re ready, take your story off the screen and into your hands."
                };
                int order = 0;
                foreach (var t in defaults)
                {
                    db.QuillMessages.Add(new MyStoryTold.Models.QuillMessage
                    {
                        Text = t, SortOrder = order++, IsActive = true, CreatedAt = DateTime.UtcNow
                    });
                }
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex3) { logger.LogWarning(ex3, "Could not seed quill messages."); }

        // Seed default memory prompts on first run
        try
        {
            if (!await db.MemoryPrompts.AnyAsync())
            {
                var prompts = new[]
                {
                    "What did you have for dinner on your wedding day?",
                    "A meal you'll never forget — who cooked it?",
                    "The first job you were proud of.",
                    "A house you lived in for less than a year.",
                    "Someone who took you in when you needed it.",
                    "The longest you've ever been alone.",
                    "A song that played at your wedding (or first dance).",
                    "A person you wish would call.",
                    "A piece of clothing you wore out from love.",
                    "The neighbor who scared you as a child.",
                    "A summer that felt like it would never end.",
                    "The car you drove until it died.",
                    "A friend you stopped talking to and miss.",
                    "Where you were when you heard about a big news event.",
                    "The hardest goodbye you've ever said.",
                    "Your favorite teacher — and what they taught you.",
                    "A trip that didn't go as planned.",
                    "Something your parent did that you only understand now.",
                    "A risk you took that paid off.",
                    "A risk you took that didn't.",
                    "The first time you felt like an adult.",
                    "A meal your grandmother made that you still crave.",
                    "Where you lived when you were 25.",
                    "Where you lived when you were 35.",
                    "A holiday you remember vividly.",
                    "The smell that takes you back the fastest.",
                    "A book that changed how you saw the world.",
                    "The kindest stranger you ever met.",
                    "A nickname you had — and who gave it to you.",
                    "Your first day at a new school or job.",
                    "A pet that meant the world to you.",
                    "The strangest place you've ever slept.",
                    "An afternoon that felt perfect.",
                    "A piece of advice that stuck.",
                    "A piece of advice you ignored.",
                    "The proudest moment of your twenties.",
                    "Something you used to do every day that you've stopped.",
                    "Something you've started doing recently.",
                    "A loss that shaped you.",
                    "A win that surprised you.",
                    "A view you'll never forget.",
                    "The longest conversation of your life.",
                    "A meal eaten in silence.",
                    "Your first love.",
                    "Your last love.",
                    "A favor someone did for you that you've never repaid.",
                    "A favor you did for someone who never knew.",
                    "What you want your grandchildren to know about you.",
                    "A secret you held for a long time.",
                    "What you'd say to your 18-year-old self in three sentences."
                };
                int order = 0;
                foreach (var p in prompts)
                {
                    db.MemoryPrompts.Add(new MyStoryTold.Models.MemoryPrompt
                    {
                        Text = p, SortOrder = order++, IsActive = true, CreatedAt = DateTime.UtcNow
                    });
                }
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex4) { logger.LogWarning(ex4, "Could not seed memory prompts."); }

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Ensure Admin role exists
        if (!await roleManager.RoleExistsAsync("Admin"))
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        if (!await roleManager.RoleExistsAsync("SuperAdmin"))
            await roleManager.CreateAsync(new IdentityRole("SuperAdmin"));

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
        if (!await userManager.IsInRoleAsync(adminUser, "SuperAdmin"))
            await userManager.AddToRoleAsync(adminUser, "SuperAdmin");
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

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<MyStoryTold.Services.LastSeenMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<MyStoryTold.Hubs.PresenceHub>("/hubs/presence");

app.Run();
