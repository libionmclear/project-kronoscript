using MyStoryTold.Models;

namespace MyStoryTold.Services;

/// <summary>
/// Static catalog of the ad-hoc Premium Services Kronoscript offers
/// (hardcover print runs, editing, photo restoration, …). These are
/// one-off paid deliverables fulfilled by partners or staff — distinct
/// from the subscription feature catalog in <see cref="IPremiumService"/>.
/// </summary>
public interface IPremiumServiceCatalog
{
    IReadOnlyList<PremiumServiceInfo> All { get; }
    PremiumServiceInfo? Get(string slug);
}

public class PremiumServiceCatalog : IPremiumServiceCatalog
{
    public IReadOnlyList<PremiumServiceInfo> All { get; } = new[]
    {
        new PremiumServiceInfo {
            Slug = "hardcover-printing",
            Name = "Hardcover printing",
            IconEmoji = "📕",
            Category = "Print",
            ShortDescription = "Your story bound in a real hardcover book.",
            LongDescription =
                "We turn your Kronoscript stories into a finished hardcover " +
                "book — printed, bound, and shipped. You pick the cover " +
                "style and the chapters; we lay out the pages, place the " +
                "photos, and proof every spread before going to print. " +
                "Standard volumes run 80–300 pages with photo plates."
        },
        new PremiumServiceInfo {
            Slug = "per-decade-booklet",
            Name = "Per-decade booklet",
            IconEmoji = "📖",
            Category = "Print",
            ShortDescription = "A short, focused booklet of one decade of your life.",
            LongDescription =
                "A 30–60 page softcover booklet that captures a single " +
                "decade — perfect as a milestone gift or a more digestible " +
                "way to share your story than a full hardcover. We curate " +
                "the strongest 15–25 stories from the years you pick, " +
                "tighten the prose, and lay them out around photos."
        },
        new PremiumServiceInfo {
            Slug = "memorial-book",
            Name = "Memorial / remembrance book",
            IconEmoji = "🕊️",
            Category = "Print",
            ShortDescription = "A hardcover tribute to a loved one who has passed.",
            LongDescription =
                "A dedicated remembrance volume — your stories, photos, " +
                "and the contributions of other family members compiled " +
                "into a single keepsake. We help collect material from " +
                "relatives, coordinate writing, and produce a printed " +
                "edition the family can keep for generations."
        },
        new PremiumServiceInfo {
            Slug = "photo-restoration",
            Name = "Photo restoration",
            IconEmoji = "🖼️",
            Category = "Multimedia",
            ShortDescription = "Human-quality restoration of damaged or faded photos.",
            LongDescription =
                "Hand-edited photo restoration by a real person — not the " +
                "lossy automatic pass you get from app filters. Repairs " +
                "tears, fades, water damage; deepens colours; recovers " +
                "faces in low-resolution scans. Standard turnaround per " +
                "photo, batched pricing available for collections."
        },
        new PremiumServiceInfo {
            Slug = "editing-ghostwriting",
            Name = "Editing / ghost-writing",
            IconEmoji = "✍️",
            Category = "Editorial",
            ShortDescription = "A real editor (or ghost-writer) on your stories.",
            LongDescription =
                "A professional editor reads your stories and helps you " +
                "tighten them — fixing pacing, structure, and continuity " +
                "without losing your voice. For people who want more " +
                "hands-on help, our ghost-writers will conduct interviews " +
                "and draft the full text from your spoken recollections."
        },
        new PremiumServiceInfo {
            Slug = "human-transcription",
            Name = "Human transcription",
            IconEmoji = "🎧",
            Category = "Editorial",
            ShortDescription = "Audio interviews converted to text by a real listener.",
            LongDescription =
                "Send us your audio interview (or have us record one) and " +
                "we'll deliver a clean text transcript — punctuated, " +
                "speaker-labelled, and pre-formatted for direct paste " +
                "into a Kronoscript story. More accurate than any " +
                "automated transcription, especially for accents, " +
                "background noise, and family-specific names."
        },
        new PremiumServiceInfo {
            Slug = "translation",
            Name = "Translation",
            IconEmoji = "🌍",
            Category = "Editorial",
            ShortDescription = "Professional translation of your stories into another language.",
            LongDescription =
                "Human translation by a native speaker so your stories " +
                "can be read by family members in another language. We " +
                "preserve idiom, names, and the warmth of the original " +
                "voice — important for memoirs that don't survive a " +
                "literal machine translation."
        },
        new PremiumServiceInfo {
            Slug = "custom-book-design",
            Name = "Custom book design",
            IconEmoji = "🎨",
            Category = "Print",
            ShortDescription = "A bespoke book design built around your story.",
            LongDescription =
                "Beyond the standard hardcover template — a designer " +
                "works with you on cover art, chapter ornaments, photo " +
                "layouts, and typography to produce a book that looks " +
                "and feels distinctive. Best for family histories, " +
                "biographies, or gift volumes where presentation matters."
        },
        new PremiumServiceInfo {
            Slug = "gift-book",
            Name = "Wedding / anniversary / birthday gift book",
            IconEmoji = "🎁",
            Category = "Print",
            ShortDescription = "A milestone-themed book printed and ready to give.",
            LongDescription =
                "A short hardcover or booklet built around a specific " +
                "milestone — a wedding, a 50th, a 90th, a child's " +
                "birthday — combining stories, photos, and tributes from " +
                "family. We can coordinate contributions across multiple " +
                "writers and have it printed in time for the date."
        },
        new PremiumServiceInfo {
            Slug = "audio-book",
            Name = "Audio book",
            IconEmoji = "🎙️",
            Category = "Multimedia",
            ShortDescription = "Your stories recorded as a professional audio book.",
            LongDescription =
                "Studio-recorded narration of your Kronoscript stories — " +
                "by you, by a family member, or by a professional voice " +
                "actor. Delivered as a downloadable file you can share " +
                "with relatives or play at a memorial / milestone event."
        },
        new PremiumServiceInfo {
            Slug = "time-capsule-mailing",
            Name = "Time-capsule mailing",
            IconEmoji = "📬",
            Category = "Other",
            ShortDescription = "A printed time-capsule mailed on a future date.",
            LongDescription =
                "Write a story today, set a date a year (or 20 years) " +
                "from now, and we'll mail a printed copy to the " +
                "recipient on that date. Works for letters to your " +
                "future grandchild, milestone birthdays, anniversary " +
                "promises — anything you'd like delivered later in the " +
                "physical world."
        },
        new PremiumServiceInfo {
            Slug = "family-tree-research",
            Name = "Family-tree research",
            IconEmoji = "🌳",
            Category = "Other",
            ShortDescription = "Genealogist help extending your tree further back.",
            LongDescription =
                "A professional genealogist works from what's already on " +
                "your Kronoscript tree and traces lineage further — " +
                "civil records, parish books, immigration manifests, " +
                "regional archives. Results are written up as a research " +
                "report you can add directly to the tree as People " +
                "Profiles."
        },
    };

    private readonly Dictionary<string, PremiumServiceInfo> _bySlug;
    public PremiumServiceCatalog()
    {
        _bySlug = All.ToDictionary(s => s.Slug, StringComparer.OrdinalIgnoreCase);
    }
    public PremiumServiceInfo? Get(string slug) =>
        _bySlug.TryGetValue(slug, out var s) ? s : null;
}
