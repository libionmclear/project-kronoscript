using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyStoryTold.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FocusX",
                table: "PostMedia",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FocusY",
                table: "PostMedia",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LayoutCol",
                table: "PostMedia",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LayoutHeight",
                table: "PostMedia",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LayoutPosition",
                table: "PostMedia",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LayoutRow",
                table: "PostMedia",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LayoutWidth",
                table: "PostMedia",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "PostMedia",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReactionType",
                table: "PostLikes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ChannelId",
                table: "LifeEventPosts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "LifeEventPosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "LifeEventPosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LayoutStyle",
                table: "LifeEventPosts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MemoryOfPostId",
                table: "LifeEventPosts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusicUrl",
                table: "LifeEventPosts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MutedUntil",
                table: "LifeEventPosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RepublishedAt",
                table: "LifeEventPosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaggedProfileIds",
                table: "LifeEventPosts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AccountDeletionRequestedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgreedToTermsAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BiographicalEra",
                table: "AspNetUsers",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BiographicalSummary",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BirthDateVisibility",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BirthPlaceVisibility",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CurrentLocationVisibility",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionCodeExpiresAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionCodeHash",
                table: "AspNetUsers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FoundingBadgeAcknowledged",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "GenderVisibility",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HideBiographicalInFeed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HideBirthYear",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HideChannelsInFeed",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBiographical",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompletelyPrivate",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LastBadgeLevelComments",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastBadgeLevelConnections",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastBadgeLevelLogins",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastBadgeLevelPosts",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastBadgeLevelWords",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LastDismissedBannerVersion",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastSeenWhatsNewVersion",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LoginDaysCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ManagedByUserId",
                table: "AspNetUsers",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MutedBiographicalUserIds",
                table: "AspNetUsers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MutedChannelIds",
                table: "AspNetUsers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nationalities",
                table: "AspNetUsers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NationalitiesVisibility",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Nickname",
                table: "AspNetUsers",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredReadingLanguage",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredUiLanguage",
                table: "AspNetUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PremiumTier",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PremiumUntil",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileCardBackgroundUrl",
                table: "AspNetUsers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecentLockoutCount",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ShowOnlineStatus",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedUntil",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IconEmoji = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    AdminUserId = table.Column<string>(type: "text", nullable: true),
                    DefaultLayoutStyle = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Channels_AspNetUsers_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CommentLikes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentLikes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentLikes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommentLikes_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommentTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    LanguageCode = table.Column<string>(type: "text", nullable: false),
                    DetectedFromLanguage = table.Column<string>(type: "text", nullable: true),
                    BodyTranslated = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentTranslations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FamilyGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatorUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyGroups_AspNetUsers_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MediaComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostMediaId = table.Column<int>(type: "integer", nullable: false),
                    AuthorUserId = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaComments_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaComments_PostMedia_PostMediaId",
                        column: x => x.PostMediaId,
                        principalTable: "PostMedia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemoryPrompts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LinkUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatorUserId = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Nickname = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Relation = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BirthYear = table.Column<int>(type: "integer", nullable: true),
                    BirthPlace = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DeathYear = table.Column<int>(type: "integer", nullable: true),
                    DeathPlace = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DatesEstimated = table.Column<bool>(type: "boolean", nullable: false),
                    Bio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Sources = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Visibility = table.Column<int>(type: "integer", nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LinkedUserId = table.Column<string>(type: "text", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimDeclinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonProfiles_AspNetUsers_CreatorUserId",
                        column: x => x.CreatorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonProfiles_AspNetUsers_LinkedUserId",
                        column: x => x.LinkedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PostTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    LanguageCode = table.Column<string>(type: "text", nullable: false),
                    DetectedFromLanguage = table.Column<string>(type: "text", nullable: true),
                    TitleTranslated = table.Column<string>(type: "text", nullable: true),
                    BodyTranslated = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostTranslations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuillMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuillMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReporterUserId = table.Column<string>(type: "text", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HandledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HandledByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reports_AspNetUsers_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "UserBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BlockerUserId = table.Column<string>(type: "text", nullable: false),
                    BlockedUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBlocks_AspNetUsers_BlockedUserId",
                        column: x => x.BlockedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserBlocks_AspNetUsers_BlockerUserId",
                        column: x => x.BlockerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkingIndexEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    MainEvent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Residence = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SchoolJob = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Relationship = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Family = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Vacation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Other = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Mood = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingIndexEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkingIndexEntries_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FamilyGroupMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FamilyGroupId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyGroupMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyGroupMembers_FamilyGroups_FamilyGroupId",
                        column: x => x.FamilyGroupId,
                        principalTable: "FamilyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FamilyGroupPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FamilyGroupId = table.Column<int>(type: "integer", nullable: false),
                    LifeEventPostId = table.Column<int>(type: "integer", nullable: false),
                    AddedByUserId = table.Column<string>(type: "text", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyGroupPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyGroupPosts_AspNetUsers_AddedByUserId",
                        column: x => x.AddedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FamilyGroupPosts_FamilyGroups_FamilyGroupId",
                        column: x => x.FamilyGroupId,
                        principalTable: "FamilyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyGroupPosts_LifeEventPosts_LifeEventPostId",
                        column: x => x.LifeEventPostId,
                        principalTable: "LifeEventPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FamilyTreeNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    NodeKind = table.Column<int>(type: "integer", nullable: false),
                    TargetUserId = table.Column<string>(type: "text", nullable: true),
                    TargetProfileId = table.Column<int>(type: "integer", nullable: true),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyTreeNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyTreeNodes_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyTreeNodes_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FamilyTreeNodes_PersonProfiles_TargetProfileId",
                        column: x => x.TargetProfileId,
                        principalTable: "PersonProfiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MediaPersonTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostMediaId = table.Column<int>(type: "integer", nullable: false),
                    TargetUserId = table.Column<string>(type: "text", nullable: true),
                    TargetProfileId = table.Column<int>(type: "integer", nullable: true),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaPersonTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaPersonTags_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MediaPersonTags_PersonProfiles_TargetProfileId",
                        column: x => x.TargetProfileId,
                        principalTable: "PersonProfiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MediaPersonTags_PostMedia_PostMediaId",
                        column: x => x.PostMediaId,
                        principalTable: "PostMedia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonProfileId = table.Column<int>(type: "integer", nullable: false),
                    ClaimantUserId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileClaims_AspNetUsers_ClaimantUserId",
                        column: x => x.ClaimantUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileClaims_PersonProfiles_PersonProfileId",
                        column: x => x.PersonProfileId,
                        principalTable: "PersonProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FamilyRelationships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    FromNodeId = table.Column<int>(type: "integer", nullable: false),
                    ToNodeId = table.Column<int>(type: "integer", nullable: false),
                    RelType = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FamilyRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FamilyRelationships_FamilyTreeNodes_FromNodeId",
                        column: x => x.FromNodeId,
                        principalTable: "FamilyTreeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FamilyRelationships_FamilyTreeNodes_ToNodeId",
                        column: x => x.ToNodeId,
                        principalTable: "FamilyTreeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifeEventPosts_ChannelId",
                table: "LifeEventPosts",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_LifeEventPosts_MemoryOfPostId",
                table: "LifeEventPosts",
                column: "MemoryOfPostId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ManagedByUserId",
                table: "AspNetUsers",
                column: "ManagedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_AdminUserId",
                table: "Channels",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_Slug",
                table: "Channels",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommentLikes_CommentId_UserId",
                table: "CommentLikes",
                columns: new[] { "CommentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommentLikes_UserId",
                table: "CommentLikes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentTranslations_CommentId_LanguageCode",
                table: "CommentTranslations",
                columns: new[] { "CommentId", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroupMembers_FamilyGroupId_UserId",
                table: "FamilyGroupMembers",
                columns: new[] { "FamilyGroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroupMembers_UserId",
                table: "FamilyGroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroupPosts_AddedByUserId",
                table: "FamilyGroupPosts",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroupPosts_FamilyGroupId_LifeEventPostId",
                table: "FamilyGroupPosts",
                columns: new[] { "FamilyGroupId", "LifeEventPostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroupPosts_LifeEventPostId",
                table: "FamilyGroupPosts",
                column: "LifeEventPostId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroups_CreatorUserId",
                table: "FamilyGroups",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRelationships_FromNodeId",
                table: "FamilyRelationships",
                column: "FromNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyRelationships_ToNodeId",
                table: "FamilyRelationships",
                column: "ToNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyTreeNodes_OwnerUserId",
                table: "FamilyTreeNodes",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyTreeNodes_TargetProfileId",
                table: "FamilyTreeNodes",
                column: "TargetProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_FamilyTreeNodes_TargetUserId",
                table: "FamilyTreeNodes",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaComments_AuthorUserId",
                table: "MediaComments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaComments_PostMediaId",
                table: "MediaComments",
                column: "PostMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaPersonTags_PostMediaId",
                table: "MediaPersonTags",
                column: "PostMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaPersonTags_TargetProfileId",
                table: "MediaPersonTags",
                column: "TargetProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaPersonTags_TargetUserId",
                table: "MediaPersonTags",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ActorUserId",
                table: "Notifications",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonProfiles_CreatorUserId",
                table: "PersonProfiles",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonProfiles_LinkedUserId",
                table: "PersonProfiles",
                column: "LinkedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PostTranslations_PostId_LanguageCode",
                table: "PostTranslations",
                columns: new[] { "PostId", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileClaims_ClaimantUserId",
                table: "ProfileClaims",
                column: "ClaimantUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileClaims_PersonProfileId",
                table: "ProfileClaims",
                column: "PersonProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReporterUserId",
                table: "Reports",
                column: "ReporterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Status_CreatedAt",
                table: "Reports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockedUserId",
                table: "UserBlocks",
                column: "BlockedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockerUserId_BlockedUserId",
                table: "UserBlocks",
                columns: new[] { "BlockerUserId", "BlockedUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkingIndexEntries_OwnerUserId",
                table: "WorkingIndexEntries",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_ManagedByUserId",
                table: "AspNetUsers",
                column: "ManagedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_LifeEventPosts_Channels_ChannelId",
                table: "LifeEventPosts",
                column: "ChannelId",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_LifeEventPosts_LifeEventPosts_MemoryOfPostId",
                table: "LifeEventPosts",
                column: "MemoryOfPostId",
                principalTable: "LifeEventPosts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_ManagedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_LifeEventPosts_Channels_ChannelId",
                table: "LifeEventPosts");

            migrationBuilder.DropForeignKey(
                name: "FK_LifeEventPosts_LifeEventPosts_MemoryOfPostId",
                table: "LifeEventPosts");

            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropTable(
                name: "CommentLikes");

            migrationBuilder.DropTable(
                name: "CommentTranslations");

            migrationBuilder.DropTable(
                name: "FamilyGroupMembers");

            migrationBuilder.DropTable(
                name: "FamilyGroupPosts");

            migrationBuilder.DropTable(
                name: "FamilyRelationships");

            migrationBuilder.DropTable(
                name: "MediaComments");

            migrationBuilder.DropTable(
                name: "MediaPersonTags");

            migrationBuilder.DropTable(
                name: "MemoryPrompts");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PostTranslations");

            migrationBuilder.DropTable(
                name: "ProfileClaims");

            migrationBuilder.DropTable(
                name: "QuillMessages");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "SiteSettings");

            migrationBuilder.DropTable(
                name: "UserBlocks");

            migrationBuilder.DropTable(
                name: "WorkingIndexEntries");

            migrationBuilder.DropTable(
                name: "FamilyGroups");

            migrationBuilder.DropTable(
                name: "FamilyTreeNodes");

            migrationBuilder.DropTable(
                name: "PersonProfiles");

            migrationBuilder.DropIndex(
                name: "IX_LifeEventPosts_ChannelId",
                table: "LifeEventPosts");

            migrationBuilder.DropIndex(
                name: "IX_LifeEventPosts_MemoryOfPostId",
                table: "LifeEventPosts");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_ManagedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FocusX",
                table: "PostMedia");

            migrationBuilder.DropColumn(
                name: "FocusY",
                table: "PostMedia");

            migrationBuilder.DropColumn(
                name: "LayoutCol",
                table: "PostMedia");

            migrationBuilder.DropColumn(
                name: "LayoutHeight",
                table: "PostMedia");

            migrationBuilder.DropColumn(
                name: "LayoutPosition",
                table: "PostMedia");

            migrationBuilder.DropColumn(
                name: "LayoutRow",
                table: "PostMedia");

            migrationBuilder.DropColumn(
                name: "LayoutWidth",
                table: "PostMedia");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "PostMedia");

            migrationBuilder.DropColumn(
                name: "ReactionType",
                table: "PostLikes");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "LifeEventPosts");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "LifeEventPosts");

            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "LifeEventPosts");

            migrationBuilder.DropColumn(
                name: "LayoutStyle",
                table: "LifeEventPosts");

            migrationBuilder.DropColumn(
                name: "MemoryOfPostId",
                table: "LifeEventPosts");

            migrationBuilder.DropColumn(
                name: "MusicUrl",
                table: "LifeEventPosts");

            migrationBuilder.DropColumn(
                name: "MutedUntil",
                table: "LifeEventPosts");

            migrationBuilder.DropColumn(
                name: "RepublishedAt",
                table: "LifeEventPosts");

            migrationBuilder.DropColumn(
                name: "TaggedProfileIds",
                table: "LifeEventPosts");

            migrationBuilder.DropColumn(
                name: "AccountDeletionRequestedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AgreedToTermsAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BiographicalEra",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BiographicalSummary",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BirthDateVisibility",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "BirthPlaceVisibility",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CurrentLocationVisibility",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DeletionCodeExpiresAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DeletionCodeHash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "FoundingBadgeAcknowledged",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GenderVisibility",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HideBiographicalInFeed",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HideBirthYear",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HideChannelsInFeed",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsBiographical",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsCompletelyPrivate",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastBadgeLevelComments",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastBadgeLevelConnections",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastBadgeLevelLogins",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastBadgeLevelPosts",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastBadgeLevelWords",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastDismissedBannerVersion",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastSeenWhatsNewVersion",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LoginDaysCount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ManagedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MutedBiographicalUserIds",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MutedChannelIds",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Nationalities",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NationalitiesVisibility",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Nickname",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PreferredReadingLanguage",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PreferredUiLanguage",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PremiumTier",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PremiumUntil",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfileCardBackgroundUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RecentLockoutCount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ShowOnlineStatus",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SuspendedUntil",
                table: "AspNetUsers");
        }
    }
}
