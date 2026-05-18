namespace MyStoryTold.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    /// <summary>Exception details — populated only when the viewer is in
    /// the Admin or SuperAdmin role. Lets us diagnose production errors
    /// without flipping the whole app into Development mode.</summary>
    public string? AdminExceptionType    { get; set; }
    public string? AdminExceptionMessage { get; set; }
    public string? AdminExceptionStack   { get; set; }
    public string? AdminPath             { get; set; }
    public bool ShowAdminException => !string.IsNullOrEmpty(AdminExceptionMessage);
}
