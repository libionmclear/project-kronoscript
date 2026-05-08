using MyStoryTold.Models;

namespace MyStoryTold.Helpers;

public static class PostMediaExtensions
{
    /// <summary>Renders post media in display order (SortOrder asc, then Id) so
    /// the home-feed cover and detail grid follow the writer's reorder choices.</summary>
    public static IEnumerable<PostMedia> Ordered(this IEnumerable<PostMedia> media)
        => media.OrderBy(m => m.SortOrder).ThenBy(m => m.Id);
}
