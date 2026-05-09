namespace MyStoryTold.Resources;

/// <summary>
/// Marker type used by <c>IStringLocalizer&lt;SharedResource&gt;</c>
/// (and <c>IHtmlLocalizer&lt;SharedResource&gt;</c>) for site-wide chrome
/// strings. The actual translations live in
/// /Resources/SharedResource.{culture}.resx. English values are the
/// resource keys themselves, so a missing translation falls back to the
/// English literal — adding a new language is purely a matter of dropping
/// in the matching .resx.
/// </summary>
public class SharedResource { }
