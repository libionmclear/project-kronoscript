using MyStoryTold.Services;

namespace MyStoryTold.Models.ViewModels;

public class UserBadgesViewModel
{
    public List<LadderProgress> Ladders { get; set; } = new();
    public FoundingBadge? Founding { get; set; }
}
