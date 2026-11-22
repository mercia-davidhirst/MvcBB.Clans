namespace MvcBB.Clans.Api
{
    public interface IClanRepository
    {
        /// <summary>Clans with member counts, ordered by SortOrder then Name.</summary>
        IReadOnlyList<Clan> GetAll(bool includeInactive);

        Clan? GetById(int id);

        /// <summary>Whether another clan (not <paramref name="exceptId"/>) already uses this name, ignoring case.</summary>
        bool NameExists(string name, int? exceptId);

        /// <summary>Whether another clan (not <paramref name="exceptId"/>) already uses this icon file.</summary>
        bool IconInUse(string iconFileName, int? exceptId);

        Clan Add(Clan clan);
        void Update(Clan clan);

        /// <summary>Deletes the clan and, first, every membership in it.</summary>
        void Remove(int id);

        /// <summary>Member id to clan id, for the given members who belong to an active clan.</summary>
        IReadOnlyDictionary<int, int> GetActiveMemberships(IReadOnlyCollection<int> userIds);

        UserClan? GetMembership(int userId);

        /// <summary>Joins a clan (replacing any current one), or leaves it when <paramref name="clanId"/> is null.</summary>
        void SetMembership(int userId, int? clanId);
    }
}
