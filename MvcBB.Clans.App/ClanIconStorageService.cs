using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MvcBB.Shared.Services;

namespace MvcBB.Clans.App
{
    public interface IClanIconStorage
    {
        /// <summary>Validates and saves an icon in the private folder, returning its stored file name.</summary>
        Task<string> SaveIconAsync(IFormFile file);

        /// <summary>Removes a stored icon. Ignores anything that isn't a name this service made.</summary>
        void DeleteIcon(string? fileName);

        /// <summary>Full path of a stored icon, or null if the name isn't one this service made or the file doesn't exist.</summary>
        string? GetIconFilePath(string fileName);
    }

    /// <summary>
    /// Clan icon uploads, following the same pattern as core's own image
    /// uploads (validate, save under a random name, return it) - duplicated
    /// here because mods can only reference MvcBB.ModKit and MvcBB.Shared.
    ///
    /// Icons are kept OUTSIDE the host's wwwroot (by default App_Data/clans
    /// under the content root, or wherever Clans:StoragePath points), so the
    /// static file middleware never serves them. ClansController.IconFile
    /// serves them instead, at /images/clans/&lt;file&gt;.
    /// </summary>
    public class ClanIconStorageService : IClanIconStorage
    {
        public const string UrlPrefix = "/images/clans/";

        /// <summary>Icons are small: shown at about 20-32 pixels.</summary>
        public const long MaxFileSizeBytes = 256 * 1024;

        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
        private static readonly string[] AllowedContentTypes = { "image/png", "image/jpeg", "image/gif", "image/webp" };
        private const string Subfolder = "clans";

        private static readonly Regex StoredFileName = new(
            @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.(png|jpg|gif|webp)\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;

        public ClanIconStorageService(IWebHostEnvironment webHostEnvironment, IConfiguration configuration)
        {
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
        }

        public async Task<string> SaveIconAsync(IFormFile file)
        {
            if (file.Length == 0)
                throw new ClanServiceException("The selected icon file is empty.");

            if (file.Length > MaxFileSizeBytes)
                throw new ClanServiceException("Icons must be smaller than 256 KB.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension) || !AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
                throw new ClanServiceException("Icons must be PNG, JPG, GIF or WebP images.");

            // Check the file's actual bytes too - the name and content type are
            // whatever the uploader chose to send. Save it with the extension
            // of what it really is, so it's always served as that image type.
            await using var upload = file.OpenReadStream();
            var detectedExtension = await DetectExtensionAsync(upload)
                ?? throw new ClanServiceException("Icons must be PNG, JPG, GIF or WebP images.");

            var folder = IconFolder;
            Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid()}{detectedExtension}";
            var filePath = ResolveInside(folder, fileName)
                ?? throw new InvalidOperationException("Icon path resolved outside the icon folder.");

            await using (var fileStream = new FileStream(filePath, FileMode.CreateNew))
            {
                await upload.CopyToAsync(fileStream);
            }

            return fileName;
        }

        public void DeleteIcon(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !StoredFileName.IsMatch(fileName))
                return;

            var filePath = ResolveInside(IconFolder, fileName);
            if (filePath != null && File.Exists(filePath))
                File.Delete(filePath);
        }

        public string? GetIconFilePath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !StoredFileName.IsMatch(fileName))
                return null;

            var filePath = ResolveInside(IconFolder, fileName);
            return filePath != null && File.Exists(filePath) ? filePath : null;
        }

        /// <summary>Content type for a stored icon, from the extension it was saved with.</summary>
        public static string ContentTypeFor(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        /// <summary>
        /// ".png", ".jpg", ".gif" (core's signature check) or ".webp" for a real
        /// image of that type, otherwise null. Leaves the stream at position 0.
        /// </summary>
        private static async Task<string?> DetectExtensionAsync(Stream stream)
        {
            var header = new byte[12];
            var read = 0;
            while (read < header.Length)
            {
                var n = await stream.ReadAsync(header.AsMemory(read, header.Length - read));
                if (n == 0) break;
                read += n;
            }

            if (stream.CanSeek)
                stream.Position = 0;
            else
                return null; // Can't rewind to save it - IFormFile streams always can.

            var bytes = header.AsSpan(0, read);
            var coreType = ImageSignature.Detect(bytes);
            if (coreType != null)
                return coreType;

            // WebP: "RIFF" <4-byte size> "WEBP"
            if (bytes.Length >= 12 &&
                bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
                bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            {
                return ".webp";
            }

            return null;
        }

        // Builds the path from the folder plus the bare file name only, then
        // confirms it still resolves inside that folder.
        private static string? ResolveInside(string folder, string fileName)
        {
            var fullFolder = Path.GetFullPath(folder);
            var filePath = Path.GetFullPath(Path.Combine(fullFolder, fileName));
            return filePath.StartsWith(fullFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? filePath
                : null;
        }

        private string IconFolder
        {
            get
            {
                var configured = _configuration["Clans:StoragePath"];
                return string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(_webHostEnvironment.ContentRootPath, "App_Data", Subfolder)
                    : Path.GetFullPath(configured, _webHostEnvironment.ContentRootPath);
            }
        }
    }
}
