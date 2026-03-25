namespace MyStoryTold.Services;

public interface IExportService
{
    Task<byte[]> ExportPostsAsDocxAsync(string userId);
    Task<byte[]> ExportPostsAsTxtAsync(string userId);
}
