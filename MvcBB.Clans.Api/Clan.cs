namespace MvcBB.Clans.Api
{
    public class Clan
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Article shown before the name ("I am a/an ..."): null picks one
        /// automatically from the name, otherwise "a", "an", or "" for none.
        /// </summary>
        public string? Article { get; set; }

        /// <summary>Stored icon file name (see <see cref="ClanIconFile"/>), or null for no icon.</summary>
        public string? IconFileName { get; set; }

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        /// <summary>Computed on read: how many members currently belong to the clan.</summary>
        public int MemberCount { get; set; }
    }

    /// <summary>One member's clan, as stored in the membership table.</summary>
    public class UserClan
    {
        public int UserId { get; set; }
        public int ClanId { get; set; }
        public DateTime JoinedAt { get; set; }
    }
}
