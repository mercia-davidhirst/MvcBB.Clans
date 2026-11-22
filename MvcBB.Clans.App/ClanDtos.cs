using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MvcBB.Clans.App
{
    // Mirrors the shapes returned/accepted by MvcBB.Clans.Api - duplicated
    // rather than shared, since a mod's two tiers share nothing beyond
    // MvcBB.ModKit (which only carries the mod framework's own types).

    public class ClanDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>null = automatic, otherwise "a", "an", or "" for none.</summary>
        public string? Article { get; set; }

        public string? IconFileName { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public int MemberCount { get; set; }

        /// <summary>"I am a Wizard." etc. Plain text: Razor encodes it.</summary>
        public string Statement => ClanArticle.Statement(Name, Article);

        /// <summary>Where the App serves the icon, or null when there isn't one.</summary>
        public string? IconUrl => IconFileName == null ? null : ClanIconStorageService.UrlPrefix + IconFileName;
    }

    /// <summary>The admin create/edit form.</summary>
    public class ClanFormModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Please give the clan a name.")]
        [StringLength(100, ErrorMessage = "The name must be 100 characters or fewer.")]
        public string Name { get; set; } = string.Empty;

        /// <summary>One of the <see cref="ClanArticle"/> modes: "auto", "a", "an" or "none".</summary>
        [Display(Name = "Article")]
        [RegularExpression("^(auto|a|an|none)$")]
        public string ArticleMode { get; set; } = ClanArticle.AutoMode;

        [Display(Name = "Sort order")]
        [Range(-100_000, 100_000)]
        public int SortOrder { get; set; }

        [Display(Name = "Active (members can choose it, and it's shown)")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Icon")]
        public IFormFile? Icon { get; set; }

        [Display(Name = "Remove the current icon")]
        public bool RemoveIcon { get; set; }

        /// <summary>Shown on the edit page only; never trusted from the post.</summary>
        public string? CurrentIconUrl { get; set; }
    }
}
