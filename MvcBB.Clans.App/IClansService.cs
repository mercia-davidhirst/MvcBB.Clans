namespace MvcBB.Clans.App
{
    public interface IClansService
    {
        /// <summary>Active clans with member counts, cached briefly in memory.</summary>
        Task<IReadOnlyList<ClanDto>> GetActiveClansAsync();

        /// <summary>Every clan, active or not (Administrator only, not cached).</summary>
        Task<IReadOnlyList<ClanDto>> GetAllClansAsync();

        /// <summary>Any clan by id (Administrator only).</summary>
        Task<ClanDto?> GetClanAsync(int id);

        Task<ClanDto> CreateClanAsync(string name, string? article, string? iconFileName, int sortOrder, bool isActive);

        /// <summary>Returns the icon file the clan no longer uses, if any, for removal from disk.</summary>
        Task<string?> UpdateClanAsync(int id, string name, string? article, string? iconFileName, int sortOrder, bool isActive);

        /// <summary>
        /// Deletes a clan, returning its icon file for removal from disk. Without
        /// <paramref name="removeMembers"/>, a clan that still has members isn't
        /// deleted and a <see cref="ClanServiceException"/> explains why.
        /// </summary>
        Task<string?> DeleteClanAsync(int id, bool removeMembers);

        /// <summary>
        /// Member id to clan id, for members in an active clan. Members without
        /// one are left out. Any number of ids: split into API-sized chunks.
        /// </summary>
        Task<IReadOnlyDictionary<int, int>> GetMembershipsAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

        /// <summary>The signed-in member's own clan id (active or not), or null.</summary>
        Task<int?> GetMyClanIdAsync();

        /// <summary>Joins an active clan, or leaves the current one with null.</summary>
        Task SetMyClanAsync(int? clanId);

        /// <summary>Drops this process's cached clan list (after an admin change).</summary>
        void InvalidateClanCache();
    }
}
