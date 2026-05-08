using Microsoft.EntityFrameworkCore;
using MyStoryTold.Data;
using MyStoryTold.Helpers;
using MyStoryTold.Models;
using MyStoryTold.Models.ViewModels;

namespace MyStoryTold.Services;

public class PostService : IPostService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IFileStorageService _files;

    public PostService(ApplicationDbContext db, IWebHostEnvironment env, IFileStorageService files)
    {
        _db = db;
        _env = env;
        _files = files;
    }

    public async Task<LifeEventPost> CreatePostAsync(string userId, CreatePostViewModel model)
    {
        var cleanBody = BodyRenderer.Sanitize(model.Body);

        // Resolve "post as" target — admins can publish on behalf of a
        // biographical/managed account they own. Anyone else's PostAsUserId
        // is silently dropped (handcrafted form post can't bypass).
        var ownerUserId = userId;
        if (!string.IsNullOrEmpty(model.PostAsUserId) && model.PostAsUserId != userId)
        {
            var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == model.PostAsUserId);
            if (target != null && target.IsBiographical && target.ManagedByUserId == userId)
            {
                ownerUserId = target.Id;
            }
        }

        // Resolve channel — only the channel's assigned writer (or the role-based
        // app-admin caller path) gets to attach a post to a channel. Channel
        // ownership is checked against the *acting* identity (admin) so an admin
        // posting as Caesar can still publish into channels Caesar owns OR
        // channels the admin owns.
        int? channelId = null;
        if (model.ChannelId.HasValue)
        {
            var channel = await _db.Channels.FindAsync(model.ChannelId.Value);
            if (channel != null && (channel.AdminUserId == userId || channel.AdminUserId == ownerUserId))
            {
                channelId = channel.Id;
            }
        }

        var post = new LifeEventPost
        {
            OwnerUserId = ownerUserId,
            Title = model.Title,
            Body = cleanBody,
            EventYear = model.EventYear,
            EventMonth = model.EventMonth,
            EventDay = model.EventDay,
            EventDateIsEstimated = model.EventDateIsEstimated,
            Visibility = model.Visibility,
            Location = model.Location,
            MusicUrl = model.MusicUrl,
            IsDraft = model.IsDraft,
            ChannelId = channelId,
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

        // Save media. The Quick Story modal funnels both photos and videos
        // through Images[], so detect type by MIME.
        if (model.Images != null)
        {
            foreach (var file in model.Images)
            {
                if (file.Length > 0)
                {
                    var type = (file.ContentType ?? "").StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                        ? MediaType.Video
                        : MediaType.Image;
                    await SaveMediaAsync(post.Id, file, type);
                }
            }
        }
        if (model.Video != null && model.Video.Length > 0)
        {
            await SaveMediaAsync(post.Id, model.Video, MediaType.Video);
        }

        // Save pasted images as proper PostMedia entries — preserve their
        // listed order so a writer's drag-reorder of pasted previews carries
        // through to the saved post.
        if (model.PastedImageUrls != null)
        {
            var nextOrder = (await _db.PostMedia.Where(m => m.PostId == post.Id)
                .Select(m => (int?)m.SortOrder).MaxAsync() ?? -1) + 1;
            foreach (var url in model.PastedImageUrls.Where(u => !string.IsNullOrWhiteSpace(u) && (u.StartsWith("/uploads/") || u.StartsWith("https://"))))
            {
                _db.PostMedia.Add(new PostMedia { PostId = post.Id, MediaType = MediaType.Image, Url = url, SortOrder = nextOrder++, CreatedAt = DateTime.UtcNow });
            }
            await _db.SaveChangesAsync();
        }

        return post;
    }

    public async Task<LifeEventPost?> GetPostAsync(int postId)
    {
        return await _db.LifeEventPosts
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments).ThenInclude(c => c.Author)
            .Include(p => p.Likes).ThenInclude(l => l.User)
            .Include(p => p.Versions.OrderByDescending(v => v.VersionNumber))
            .Include(p => p.Channel)
            .FirstOrDefaultAsync(p => p.Id == postId);
    }

    public async Task<LifeEventPost?> EditPostAsync(int postId, string userId, EditPostViewModel model)
    {
        var post = await _db.LifeEventPosts
            .Include(p => p.Versions)
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == postId);
        if (post == null) return null;
        // Direct ownership OR an admin managing a biographical account that
        // owns the post (used when an admin edits a "Caesar"-type post).
        var canManage = post.OwnerUserId == userId
            || (post.Owner != null && post.Owner.IsBiographical
                && !string.IsNullOrEmpty(post.Owner.ManagedByUserId)
                && post.Owner.ManagedByUserId == userId);
        if (!canManage) return null;

        var cleanBody = BodyRenderer.Sanitize(model.Body);
        post.Title = model.Title;
        post.Body = cleanBody;
        post.EventYear = model.EventYear;
        post.EventMonth = model.EventMonth;
        post.EventDay = model.EventDay;
        post.EventDateIsEstimated = model.EventDateIsEstimated;
        post.Visibility = model.Visibility;
        post.Location = model.Location;
        post.IsDraft = model.IsDraft;
        post.TaggedUserIds = model.TaggedUserIds != null && model.TaggedUserIds.Count > 0
            ? string.Join(",", model.TaggedUserIds)
            : null;
        post.LastEditedAt = DateTime.UtcNow;
        post.CurrentVersionNumber++;

        var version = new PostVersion
        {
            PostId = post.Id,
            VersionNumber = post.CurrentVersionNumber,
            BodySnapshot = cleanBody,
            TitleSnapshot = model.Title,
            EditedAt = DateTime.UtcNow,
            EditedByUserId = userId
        };
        _db.PostVersions.Add(version);

        // Re-stamp existing media SortOrder per the user's drag-reorder. Items
        // missing from MediaOrder keep their existing SortOrder; new media
        // (added below) lands AFTER the highest re-stamped value.
        // Same loop also picks up the click-to-focus selection per tile.
        if (model.MediaOrder != null && model.MediaOrder.Count > 0)
        {
            var existing = await _db.PostMedia.Where(m => m.PostId == post.Id).ToListAsync();
            var byId = existing.ToDictionary(m => m.Id);
            for (int i = 0; i < model.MediaOrder.Count; i++)
            {
                if (byId.TryGetValue(model.MediaOrder[i], out var m))
                {
                    m.SortOrder = i;
                    if (model.MediaFocusX != null && i < model.MediaFocusX.Count)
                        m.FocusX = Math.Clamp(model.MediaFocusX[i], 0, 100);
                    if (model.MediaFocusY != null && i < model.MediaFocusY.Count)
                        m.FocusY = Math.Clamp(model.MediaFocusY[i], 0, 100);
                }
            }
        }

        // Save new media — they go to the end via SaveMediaAsync's max-+-1 logic.
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

        if (model.PastedImageUrls != null)
        {
            var nextOrder = (await _db.PostMedia.Where(m => m.PostId == post.Id)
                .Select(m => (int?)m.SortOrder).MaxAsync() ?? -1) + 1;
            foreach (var url in model.PastedImageUrls.Where(u => !string.IsNullOrWhiteSpace(u) && (u.StartsWith("/uploads/") || u.StartsWith("https://"))))
            {
                _db.PostMedia.Add(new PostMedia { PostId = post.Id, MediaType = MediaType.Image, Url = url, SortOrder = nextOrder++, CreatedAt = DateTime.UtcNow });
            }
        }

        await _db.SaveChangesAsync();
        return post;
    }

    public async Task<List<LifeEventPost>> GetTimelinePostsAsync(string ownerUserId, string sortBy, FriendTier? viewerTier, bool isOwner)
    {
        // Channel posts are editorial content owned by the channel, not by the
        // writer. They appear in the channel context only — never on the
        // writer's personal timeline ("My Story").
        var query = _db.LifeEventPosts
            .Where(p => p.OwnerUserId == ownerUserId && p.ChannelId == null)
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments)
            .Include(p => p.Likes).ThenInclude(l => l.User)
            .Include(p => p.Channel)
            .Include(p => p.Versions.OrderByDescending(v => v.VersionNumber).Take(2))
            .AsQueryable();

        if (!isOwner)
        {
            // Hide drafts from anyone but the owner
            query = query.Where(p => !p.IsDraft);
            query = query.Where(p =>
                p.Visibility == PostVisibility.Public ||
                (p.Visibility == PostVisibility.Acquaintances && viewerTier != null) ||
                (p.Visibility == PostVisibility.Friends && (viewerTier == FriendTier.Friend || viewerTier == FriendTier.Family)) ||
                (p.Visibility == PostVisibility.Family && viewerTier == FriendTier.Family)
            );
        }

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
        // Get accepted friend connections with tier info
        var friendConnections = await _db.FriendConnections
            .Where(f => f.Status == FriendConnectionStatus.Accepted)
            .Where(f => f.RequesterUserId == userId || f.AddresseeUserId == userId)
            .Select(f => new
            {
                FriendId = f.RequesterUserId == userId ? f.AddresseeUserId : f.RequesterUserId,
                f.Tier
            })
            .ToListAsync();

        var tierMap = friendConnections.ToDictionary(f => f.FriendId, f => f.Tier);

        // Also include accepted relatives — treat them as Family tier
        var relativeIds = await _db.RelativeConnections
            .Where(r => r.Status == RelativeConnectionStatus.Accepted)
            .Where(r => r.UserAId == userId || r.UserBId == userId)
            .Select(r => r.UserAId == userId ? r.UserBId : r.UserAId)
            .ToListAsync();

        foreach (var rid in relativeIds)
        {
            if (!tierMap.ContainsKey(rid))
                tierMap[rid] = FriendTier.Family;
        }

        var allIds = tierMap.Keys.ToList();

        // Posts from connections (respecting tier-based visibility)
        var connectionPosts = await _db.LifeEventPosts
            .Where(p => allIds.Contains(p.OwnerUserId))
            .Where(p => p.Visibility != PostVisibility.Private)
            .Where(p => !p.IsDraft)
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments)
            .Include(p => p.Likes).ThenInclude(l => l.User)
            .Include(p => p.Channel)
            .OrderByDescending(p => p.CreatedAt)
            .Take(100)
            .ToListAsync();

        var filtered = connectionPosts.Where(p =>
        {
            if (!tierMap.TryGetValue(p.OwnerUserId, out var tier)) return false;
            return p.Visibility == PostVisibility.Public ||
                   p.Visibility == PostVisibility.Acquaintances ||
                   (p.Visibility == PostVisibility.Friends && (tier == FriendTier.Friend || tier == FriendTier.Family)) ||
                   (p.Visibility == PostVisibility.Family && tier == FriendTier.Family);
        }).ToList();

        // Also surface public posts from non-connected users (discovery)
        var excludeIds = allIds.Concat(new[] { userId }).ToList();
        var publicPosts = await _db.LifeEventPosts
            .Where(p => !excludeIds.Contains(p.OwnerUserId))
            .Where(p => p.Visibility == PostVisibility.Public)
            .Where(p => !p.IsDraft)
            .Include(p => p.Owner)
            .Include(p => p.Media)
            .Include(p => p.Comments)
            .Include(p => p.Likes).ThenInclude(l => l.User)
            .Include(p => p.Channel)
            .OrderByDescending(p => p.CreatedAt)
            .Take(50)
            .ToListAsync();

        return filtered
            .Concat(publicPosts)
            .OrderByDescending(p => p.CreatedAt)
            .ToList();
    }

    public async Task<Comment> AddCommentAsync(string userId, AddCommentViewModel model)
    {
        // Parse @mentions from body
        var mentionedIds = new List<string>();
        var matches = System.Text.RegularExpressions.Regex.Matches(model.Body, @"@(\w+)");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var username = match.Groups[1].Value;
            var mentioned = await _db.Users.FirstOrDefaultAsync(u => u.UserName == username);
            if (mentioned != null && !mentionedIds.Contains(mentioned.Id))
                mentionedIds.Add(mentioned.Id);
        }

        var comment = new Comment
        {
            PostId = model.PostId,
            ParentCommentId = model.ParentCommentId,
            AuthorUserId = userId,
            Body = model.Body,
            MentionedUserIds = mentionedIds.Count > 0 ? string.Join(",", mentionedIds) : null,
            EventYear = model.EventYear ?? 0,
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
        // Backwards-compat shim used by form-based ToggleLike: simple Heart toggle.
        var (reaction, _) = await ToggleReactionAsync(postId, userId, ReactionType.Heart);
        return reaction != null;
    }

    public async Task<(ReactionType? reaction, int count)> ToggleReactionAsync(int postId, string userId, ReactionType reactionType)
    {
        var existing = await _db.PostLikes
            .FirstOrDefaultAsync(l => l.PostId == postId && l.UserId == userId);

        ReactionType? result;
        if (existing == null)
        {
            _db.PostLikes.Add(new PostLike
            {
                PostId = postId,
                UserId = userId,
                ReactionType = reactionType,
                CreatedAt = DateTime.UtcNow
            });
            result = reactionType;
        }
        else if (existing.ReactionType == reactionType)
        {
            _db.PostLikes.Remove(existing);
            result = null;
        }
        else
        {
            existing.ReactionType = reactionType;
            existing.CreatedAt = DateTime.UtcNow;
            result = reactionType;
        }

        await _db.SaveChangesAsync();
        var count = await _db.PostLikes.CountAsync(l => l.PostId == postId);
        return (result, count);
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
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        using var stream = file.OpenReadStream();
        var url = await _files.UploadAsync(stream, "", fileName, file.ContentType);

        var nextOrder = (await _db.PostMedia.Where(m => m.PostId == postId)
            .Select(m => (int?)m.SortOrder).MaxAsync() ?? -1) + 1;

        _db.PostMedia.Add(new PostMedia
        {
            PostId = postId,
            MediaType = mediaType,
            Url = url,
            SortOrder = nextOrder,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
