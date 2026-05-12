using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyStoryTold.Models;

namespace MyStoryTold.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<FriendConnection> FriendConnections => Set<FriendConnection>();
    public DbSet<RelativeConnection> RelativeConnections => Set<RelativeConnection>();
    public DbSet<LifeEventPost> LifeEventPosts => Set<LifeEventPost>();
    public DbSet<PersonProfile> PersonProfiles => Set<PersonProfile>();
    public DbSet<PostVersion> PostVersions => Set<PostVersion>();
    public DbSet<PostMedia> PostMedia => Set<PostMedia>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<PostLike> PostLikes => Set<PostLike>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<UserBan> UserBans => Set<UserBan>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Tip> Tips => Set<Tip>();
    public DbSet<MediaComment> MediaComments => Set<MediaComment>();
    public DbSet<WorkingIndexEntry> WorkingIndexEntries => Set<WorkingIndexEntry>();
    public DbSet<QuillMessage> QuillMessages => Set<QuillMessage>();
    public DbSet<MemoryPrompt> MemoryPrompts => Set<MemoryPrompt>();
    public DbSet<PostTranslation> PostTranslations => Set<PostTranslation>();
    public DbSet<CommentTranslation> CommentTranslations => Set<CommentTranslation>();
    public DbSet<CommentLike> CommentLikes => Set<CommentLike>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<FamilyTreeNode> FamilyTreeNodes => Set<FamilyTreeNode>();
    public DbSet<FamilyRelationship> FamilyRelationships => Set<FamilyRelationship>();
    public DbSet<MediaPersonTag> MediaPersonTags => Set<MediaPersonTag>();
    public DbSet<ProfileClaim> ProfileClaims => Set<ProfileClaim>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // FriendConnection: two FKs to ApplicationUser
        builder.Entity<FriendConnection>(e =>
        {
            e.HasOne(f => f.Requester)
                .WithMany(u => u.SentFriendRequests)
                .HasForeignKey(f => f.RequesterUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(f => f.Addressee)
                .WithMany(u => u.ReceivedFriendRequests)
                .HasForeignKey(f => f.AddresseeUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(f => new { f.RequesterUserId, f.AddresseeUserId }).IsUnique();
        });

        // RelativeConnection
        builder.Entity<RelativeConnection>(e =>
        {
            e.HasOne(r => r.UserA)
                .WithMany(u => u.RelativeConnectionsA)
                .HasForeignKey(r => r.UserAId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.UserB)
                .WithMany(u => u.RelativeConnectionsB)
                .HasForeignKey(r => r.UserBId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(r => new { r.UserAId, r.UserBId }).IsUnique();
        });

        // LifeEventPost
        builder.Entity<LifeEventPost>(e =>
        {
            e.HasOne(p => p.Owner)
                .WithMany(u => u.Posts)
                .HasForeignKey(p => p.OwnerUserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Soft-delete: hide DeletedAt rows from every normal query. The archive
            // view (Posts/Deleted) opts in via .IgnoreQueryFilters().
            e.HasQueryFilter(p => p.DeletedAt == null);
        });

        // PostVersion
        builder.Entity<PostVersion>(e =>
        {
            e.HasOne(v => v.Post)
                .WithMany(p => p.Versions)
                .HasForeignKey(v => v.PostId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(v => v.EditedBy)
                .WithMany()
                .HasForeignKey(v => v.EditedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // PostMedia
        builder.Entity<PostMedia>(e =>
        {
            e.HasOne(m => m.Post)
                .WithMany(p => p.Media)
                .HasForeignKey(m => m.PostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Comment
        builder.Entity<Comment>(e =>
        {
            e.HasOne(c => c.Post)
                .WithMany(p => p.Comments)
                .HasForeignKey(c => c.PostId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.Author)
                .WithMany(u => u.Comments)
                .HasForeignKey(c => c.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // PostLike
        builder.Entity<PostLike>(e =>
        {
            e.HasOne(l => l.Post)
                .WithMany(p => p.Likes)
                .HasForeignKey(l => l.PostId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.User)
                .WithMany(u => u.Likes)
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(l => new { l.PostId, l.UserId }).IsUnique();
        });

        // PostTranslation — one row per (post, target language)
        builder.Entity<PostTranslation>(e =>
        {
            e.HasIndex(t => new { t.PostId, t.LanguageCode }).IsUnique();
        });

        // CommentTranslation — one row per (comment, target language)
        builder.Entity<CommentTranslation>(e =>
        {
            e.HasIndex(t => new { t.CommentId, t.LanguageCode }).IsUnique();
        });

        // Notification — recipient + (optional) actor; index by recipient and time for fast feed reads
        builder.Entity<Notification>(e =>
        {
            e.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(n => n.Actor)
                .WithMany()
                .HasForeignKey(n => n.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(n => new { n.UserId, n.CreatedAt });
        });

        // UserBlock — one row per (blocker, blocked); both restrict-on-delete because
        // we don't want a deleted user to disappear silently from a block list.
        builder.Entity<UserBlock>(e =>
        {
            e.HasIndex(b => new { b.BlockerUserId, b.BlockedUserId }).IsUnique();
            e.HasOne(b => b.Blocker)
                .WithMany()
                .HasForeignKey(b => b.BlockerUserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Blocked)
                .WithMany()
                .HasForeignKey(b => b.BlockedUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Report>(e =>
        {
            e.HasIndex(r => new { r.Status, r.CreatedAt });
            e.HasOne(r => r.Reporter)
                .WithMany()
                .HasForeignKey(r => r.ReporterUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Channel — admin-curated topical bucket. AdminUserId is nullable
        // (channel can exist without an assigned writer; only app-admins post
        // until one is assigned). Slug is unique so /Channel/{slug} works.
        builder.Entity<Channel>(e =>
        {
            e.HasIndex(c => c.Slug).IsUnique();
            e.HasOne(c => c.Admin)
                .WithMany()
                .HasForeignKey(c => c.AdminUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // LifeEventPost.Channel relationship — set null on channel delete so
        // the post survives even if the channel is removed.
        builder.Entity<LifeEventPost>(e =>
        {
            e.HasOne(p => p.Channel)
                .WithMany()
                .HasForeignKey(p => p.ChannelId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ApplicationUser self-reference: ManagedByUserId points to the admin
        // who owns a biographical/managed account. Set null on admin delete so
        // the biographical profiles survive (they can be re-claimed by another
        // admin from the Managed Users page).
        builder.Entity<ApplicationUser>(e =>
        {
            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(u => u.ManagedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(u => u.ManagedByUserId);
        });

        // CommentLike — one heart per (comment, user); cascade delete on the comment
        builder.Entity<CommentLike>(e =>
        {
            e.HasOne(l => l.Comment)
                .WithMany()
                .HasForeignKey(l => l.CommentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(l => l.User)
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(l => new { l.CommentId, l.UserId }).IsUnique();
        });

        // Message
        builder.Entity<Message>(e =>
        {
            e.HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(m => m.Recipient)
                .WithMany()
                .HasForeignKey(m => m.RecipientUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
