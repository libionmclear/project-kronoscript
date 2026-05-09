namespace MyStoryTold;

/// <summary>
/// Marker type used by <c>IStringLocalizer&lt;SharedResource&gt;</c>
/// (and <c>IHtmlLocalizer&lt;SharedResource&gt;</c>) for site-wide chrome
/// strings. Translations live at /Resources/SharedResource.{culture}.resx.
///
/// IMPORTANT: this class lives in the assembly root namespace
/// (MyStoryTold, not MyStoryTold.Resources). Combined with
/// `AddLocalization(opt => opt.ResourcesPath = "Resources")` in Program.cs,
/// the resource baseName resolves to
/// `MyStoryTold.Resources.SharedResource.{culture}.resources` — exactly
/// what the SDK embeds when the .resx sits at `/Resources/`.
/// Putting this class under `MyStoryTold.Resources` would push the lookup
/// to `Resources/Resources/SharedResource.{culture}.resx`, which doesn't
/// exist, and every key would silently fall back to its English default.
///
/// Keys ARE the English strings, so missing translations fall back to
/// the literal — adding a new language is purely dropping in a matching
/// .resx file.
/// </summary>
public class SharedResource { }
