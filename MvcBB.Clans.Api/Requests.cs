using System.ComponentModel.DataAnnotations;

namespace MvcBB.Clans.Api
{
    public class SaveClanRequest
    {
        [Required, StringLength(100, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;

        /// <summary>null = automatic, otherwise "a", "an", or "" for no article.</summary>
        [RegularExpression("^(a|an)?$", ErrorMessage = "The article must be \"a\", \"an\", empty, or left automatic.")]
        public string? Article { get; set; }

        [RegularExpression(ClanIconFile.Pattern, ErrorMessage = "Invalid icon file name.")]
        public string? IconFileName { get; set; }

        [Range(-100_000, 100_000)]
        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class SetMyClanRequest
    {
        /// <summary>The clan to join, or null to leave the current one.</summary>
        public int? ClanId { get; set; }
    }
}
