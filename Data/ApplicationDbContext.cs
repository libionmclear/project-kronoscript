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
