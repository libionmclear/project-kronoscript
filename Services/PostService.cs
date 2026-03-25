using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Services;

public class PostService : IPostService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public PostService(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public async Task<LifeEventPost> CreatePostAsync(string userId, CreatePostViewModel model)
    {
        var post = new LifeEventPost
        {
            OwnerUserId = userId,
            Title = model.Title,
            Body = model.Body,
            EventYear = model.EventYear,
            EventMonth = model.EventMonth,
            EventDay = model.EventDay,
            EventDateIsEstimated = model.EventDateIsEstimated,
            CreatedAt = DateTime.UtcNow,
            CurrentVersionNumber = 1,
            TaggedUserIds = model.TaggedUserIds != null ? string.Join(",", model.TaggedUserIds) : null
        };

        _db.LifeEventPosts.Add(post);
        await _db.SaveChangesAsync();

        // Save initial version
        var version = new PostVersion
        {
            PostId = post.Id,
            VersionNumber = 1,
            BodySnapshot = post.Body,
            TitleSnapshot = post.Title,
            EditedAt = post.CreatedAt,
            EditedByUserId = userId
        };
        _db.PostVersions.Add(version);
        await _db.SaveChangesAsync();

        // Save media
        if (model.Images != null)
        {
            foreach (var img in model.Images)
            {
                if (img.Length > 0)
                    await SaveMediaAsync(post.Id, img, MediaType.Image);
            }
        }
        if (model.Video != null && model.Video.Length > 0)
        {
            await SaveMediaAsync(post.Id, model.Video, MediaType.Video);
        }

        return post;
    }

    public async Task<LifeEventPost?> GetPostAsync(int postId)
    {
        return await _db.LifeEventPosts
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments).ThenInclude(c => c.Author)
            .Include(p => p.Likes)
            .Include(p => p.Versions.OrderByDescending(v => v.VersionNumber))
            .FirstOrDefaultAsync(p => p.Id == postId);
    }

    public async Task<LifeEventPost?> EditPostAsync(int postId, string userId, EditPostViewModel model)
    {
        var post = await _db.LifeEventPosts.Include(p => p.Versions).FirstOrDefaultAsync(p => p.Id == postId);
        if (post == null || post.OwnerUserId != userId) return null;

        post.Title = model.Title;
        post.Body = model.Body;
        post.EventYear = model.EventYear;
        post.EventMonth = model.EventMonth;
        post.EventDay = model.EventDay;
        post.EventDateIsEstimated = model.EventDateIsEstimated;
        post.LastEditedAt = DateTime.UtcNow;
        post.CurrentVersionNumber++;

        var version = new PostVersion
        {
            PostId = post.Id,
            VersionNumber = post.CurrentVersionNumber,
            BodySnapshot = model.Body,
            TitleSnapshot = model.Title,
            EditedAt = DateTime.UtcNow,
            EditedByUserId = userId
        };
        _db.PostVersions.Add(version);

        // Save new media
        if (model.Images != null)
        {
            foreach (var img in model.Images)
            {
                if (img.Length > 0)
                    await SaveMediaAsync(post.Id, img, MediaType.Image);
            }
        }
        if (model.Video != null && model.Video.Length > 0)
        {
            await SaveMediaAsync(post.Id, model.Video, MediaType.Video);
        }

        await _db.SaveChangesAsync();
        return post;
    }

    public async Task<List<LifeEventPost>> GetTimelinePostsAsync(string ownerUserId, string sortBy)
    {
        var query = _db.LifeEventPosts
            .Where(p => p.OwnerUserId == ownerUserId)
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments)
            .Include(p => p.Likes)
            .Include(p => p.Versions.OrderByDescending(v => v.VersionNumber).Take(2))
            .AsQueryable();

        if (sortBy == "event")
        {
            query = query.OrderBy(p => p.EventYear)
                         .ThenBy(p => p.EventMonth)
                         .ThenBy(p => p.EventDay);
        }
        else
        {
            query = query.OrderByDescending(p => p.CreatedAt);
        }

        return await query.ToListAsync();
    }

    public async Task<List<LifeEventPost>> GetFeedPostsAsync(string userId)
    {
        // Get all accepted friend IDs
        var friendIds = await _db.FriendConnections
            .Where(f => f.Status == FriendConnectionStatus.Accepted)
            .Where(f => f.RequesterUserId == userId || f.AddresseeUserId == userId)
            .Select(f => f.RequesterUserId == userId ? f.AddresseeUserId : f.RequesterUserId)
            .ToListAsync();

        return await _db.LifeEventPosts
            .Where(p => friendIds.Contains(p.OwnerUserId))
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments)
            .Include(p => p.Likes)
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .ToListAsync();
    }

    public async Task<Comment> AddCommentAsync(string userId, AddCommentViewModel model)
    {
        var comment = new Comment
        {
            PostId = model.PostId,
            AuthorUserId = userId,
            Body = model.Body,
            EventYear = model.EventYear,
            EventMonth = model.EventMonth,
            EventDay = model.EventDay,
            EventDateIsEstimated = model.EventDateIsEstimated,
            CreatedAt = DateTime.UtcNow
        };

        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();
        return comment;
    }

    public async Task<bool> ToggleLikeAsync(int postId, string userId)
    {
        var existing = await _db.PostLikes
            .FirstOrDefaultAsync(l => l.PostId == postId && l.UserId == userId);

        if (existing != null)
        {
            _db.PostLikes.Remove(existing);
            await _db.SaveChangesAsync();
            return false; // unliked
        }

        _db.PostLikes.Add(new PostLike
        {
            PostId = postId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return true; // liked
    }

    public async Task<bool> ReorderPostAsync(int postId, int newOrder, string userId)
    {
        var post = await _db.LifeEventPosts.FindAsync(postId);
        if (post == null) return false;

        post.StoryOrder = newOrder;
        post.LastReorderedByUserId = userId;
        post.LastReorderedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task SaveMediaAsync(int postId, IFormFile file, MediaType mediaType)
    {
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadsDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        _db.PostMedia.Add(new PostMedia
        {
            PostId = postId,
            MediaType = mediaType,
            Url = $"/uploads/{fileName}",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
