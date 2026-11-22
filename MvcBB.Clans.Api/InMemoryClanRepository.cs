using MvcBB.API.InMemory;
using MvcBB.Shared.Models.User;

namespace MvcBB.Clans.Api
{
    public class InMemoryClanRepository : IClanRepository
    {
        private readonly ClansInMemoryStore _store;
        private readonly InMemoryStore _coreStore;

        public InMemoryClanRepository(ClansInMemoryStore store, InMemoryStore coreStore)
        {
            _store = store;
            _coreStore = coreStore;
        }

        // Anonymized (Deleted) accounts never show a clan or count as members,
        // matching the SQL Server and PostgreSQL repositories.
        private bool IsShownMember(int userId) =>
            _coreStore.Users.FirstOrDefault(u => u.Id == userId) is { } user && user.Role != UserRole.Deleted;

        public IReadOnlyList<Clan> GetAll(bool includeInactive)
        {
            lock (_store.Lock)
            {
                return _store.Clans
                    .Where(c => includeInactive || c.IsActive)
                    .OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(Copy)
                    .ToList();
            }
        }

        public Clan? GetById(int id)
        {
            lock (_store.Lock)
            {
                var clan = _store.Clans.FirstOrDefault(c => c.Id == id);
                return clan == null ? null : Copy(clan);
            }
        }

        public bool NameExists(string name, int? exceptId)
        {
            lock (_store.Lock)
            {
                return _store.Clans.Any(c => c.Id != exceptId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        public bool IconInUse(string iconFileName, int? exceptId)
        {
            lock (_store.Lock)
            {
                return _store.Clans.Any(c => c.Id != exceptId && string.Equals(c.IconFileName, iconFileName, StringComparison.OrdinalIgnoreCase));
            }
        }

        public Clan Add(Clan clan)
        {
            lock (_store.Lock)
            {
                var stored = new Clan
                {
                    Id = _store.Clans.Count == 0 ? 1 : _store.Clans.Max(c => c.Id) + 1,
                    Name = clan.Name,
                    Article = clan.Article,
                    IconFileName = clan.IconFileName,
                    SortOrder = clan.SortOrder,
                    IsActive = clan.IsActive,
                    CreatedAt = DateTime.UtcNow
                };
                _store.Clans.Add(stored);
                return Copy(stored);
            }
        }

        public void Update(Clan clan)
        {
            lock (_store.Lock)
            {
                var existing = _store.Clans.FirstOrDefault(c => c.Id == clan.Id);
                if (existing == null) return;
                existing.Name = clan.Name;
                existing.Article = clan.Article;
                existing.IconFileName = clan.IconFileName;
                existing.SortOrder = clan.SortOrder;
                existing.IsActive = clan.IsActive;
            }
        }

        public void Remove(int id)
        {
            lock (_store.Lock)
            {
                foreach (var userId in _store.Memberships.Where(m => m.Value.ClanId == id).Select(m => m.Key).ToList())
                    _store.Memberships.Remove(userId);
                _store.Clans.RemoveAll(c => c.Id == id);
            }
        }

        public IReadOnlyDictionary<int, int> GetActiveMemberships(IReadOnlyCollection<int> userIds)
        {
            lock (_store.Lock)
            {
                var activeIds = _store.Clans.Where(c => c.IsActive).Select(c => c.Id).ToHashSet();
                var result = new Dictionary<int, int>();
                foreach (var userId in userIds)
                {
                    if (_store.Memberships.TryGetValue(userId, out var membership) && activeIds.Contains(membership.ClanId) && IsShownMember(userId))
                        result[userId] = membership.ClanId;
                }
                return result;
            }
        }

        public UserClan? GetMembership(int userId)
        {
            lock (_store.Lock)
            {
                return _store.Memberships.TryGetValue(userId, out var membership)
                    ? new UserClan { UserId = membership.UserId, ClanId = membership.ClanId, JoinedAt = membership.JoinedAt }
                    : null;
            }
        }

        public void SetMembership(int userId, int? clanId)
        {
            lock (_store.Lock)
            {
                if (clanId == null)
                {
                    _store.Memberships.Remove(userId);
                    return;
                }

                if (_store.Memberships.TryGetValue(userId, out var existing) && existing.ClanId == clanId.Value)
                    return;

                _store.Memberships[userId] = new UserClan { UserId = userId, ClanId = clanId.Value, JoinedAt = DateTime.UtcNow };
            }
        }

        // Hands out copies, so callers can't change the shared store by accident.
        private Clan Copy(Clan clan) => new()
        {
            Id = clan.Id,
            Name = clan.Name,
            Article = clan.Article,
            IconFileName = clan.IconFileName,
            SortOrder = clan.SortOrder,
            IsActive = clan.IsActive,
            CreatedAt = clan.CreatedAt,
            MemberCount = _store.Memberships.Values.Count(m => m.ClanId == clan.Id && IsShownMember(m.UserId))
        };
    }
}
