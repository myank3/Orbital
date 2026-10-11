using Microsoft.AspNetCore.Http;

namespace Orbital.Service;

public class FileStorageService
{
    private readonly IWebHostEnvironment _env;

    public FileStorageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<string?> SaveAsync(IFormFile file, string folder, long maxBytes = 5 * 1024 * 1024)
    {
        if (file == null || file.Length == 0) return null;
        if (file.Length > maxBytes) return null;

        // Match the static-file mapping in Program.cs:
        // write into <ContentRoot>/<folder>. On MonsterASP,
        // ContentRootPath is the site folder; there's no nested wwwroot.
        var targetDir = Path.Combine(_env.ContentRootPath, folder);
        Directory.CreateDirectory(targetDir);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(ext)) ext = ".bin";

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(targetDir, fileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        return $"/{folder}/{fileName}";
    }
}