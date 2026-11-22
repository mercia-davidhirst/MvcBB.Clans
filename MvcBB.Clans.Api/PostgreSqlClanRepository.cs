using Dapper;
using MvcBB.Shared.Models.User;
using MvcBB.API.PostgreSql;

namespace MvcBB.Clans.Api
{
    // Column names map to properties via Dapper.DefaultTypeMap.MatchNamesWithUnderscores,
    // which core's PostgreSQL provider turns on.
    public class PostgreSqlClanRepository : IClanRepository
    {
        private const string ClanSelect = @"
            SELECT c.id, c.name, c.article, c.icon_file_name, c.sort_order, c.is_active, c.created_at,
                   (SELECT COUNT(*)::int FROM public.mod_clans_user_clans uc INNER JOIN public.users u ON u.id = uc.user_id
                    WHERE uc.clan_id = c.id AND u.role <> 3) AS member_count
            FROM public.mod_clans_clans c";

        private readonly IPostgreSqlConnectionFactory _connectionFactory;

        public PostgreSqlClanRepository(IPostgreSqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public IReadOnlyList<Clan> GetAll(bool includeInactive)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.Query<Clan>(ClanSelect + @"
                WHERE (@IncludeInactive OR c.is_active)
                ORDER BY c.sort_order, c.name", new { IncludeInactive = includeInactive }).ToList();
        }

        public Clan? GetById(int id)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.QuerySingleOrDefault<Clan>(ClanSelect + " WHERE c.id = @Id", new { Id = id });
        }

        public bool NameExists(string name, int? exceptId)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.ExecuteScalar<int>(@"
                SELECT COUNT(*)::int FROM public.mod_clans_clans
                WHERE LOWER(name) = LOWER(@Name) AND (@ExceptId::int IS NULL OR id <> @ExceptId::int)",
                new { Name = name, ExceptId = exceptId }) > 0;
        }

        public bool IconInUse(string iconFileName, int? exceptId)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.ExecuteScalar<int>(@"
                SELECT COUNT(*)::int FROM public.mod_clans_clans
                WHERE icon_file_name = @IconFileName AND (@ExceptId::int IS NULL OR id <> @ExceptId::int)",
                new { IconFileName = iconFileName, ExceptId = exceptId }) > 0;
        }

        public Clan Add(Clan clan)
        {
            using var connection = _connectionFactory.CreateConnection();
            var id = connection.QuerySingle<int>(@"
                INSERT INTO public.mod_clans_clans (name, article, icon_file_name, sort_order, is_active, created_at)
                VALUES (@Name, @Article, @IconFileName, @SortOrder, @IsActive, (NOW() AT TIME ZONE 'utc'))
                RETURNING id",
                new { clan.Name, clan.Article, clan.IconFileName, clan.SortOrder, clan.IsActive });

            return GetById(id)!;
        }

        public void Update(Clan clan)
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(@"
                UPDATE public.mod_clans_clans
                SET name = @Name, article = @Article, icon_file_name = @IconFileName, sort_order = @SortOrder, is_active = @IsActive
                WHERE id = @Id",
                new { clan.Id, clan.Name, clan.Article, clan.IconFileName, clan.SortOrder, clan.IsActive });
        }

        public void Remove(int id)
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute("DELETE FROM public.mod_clans_user_clans WHERE clan_id = @Id", new { Id = id }, transaction);
            connection.Execute("DELETE FROM public.mod_clans_clans WHERE id = @Id", new { Id = id }, transaction);
            transaction.Commit();
        }

        public IReadOnlyDictionary<int, int> GetActiveMemberships(IReadOnlyCollection<int> userIds)
        {
            if (userIds.Count == 0) return new Dictionary<int, int>();

            using var connection = _connectionFactory.CreateConnection();
            return connection.Query<(int UserId, int ClanId)>(@"
                SELECT uc.user_id, uc.clan_id
                FROM public.mod_clans_user_clans uc
                INNER JOIN public.mod_clans_clans c ON c.id = uc.clan_id
                INNER JOIN public.users u ON u.id = uc.user_id
                WHERE c.is_active AND u.role <> @DeletedRole AND uc.user_id = ANY(@UserIds)",
                new { UserIds = userIds.ToArray(), DeletedRole = (short)UserRole.Deleted }).ToDictionary(r => r.UserId, r => r.ClanId);
        }

        public UserClan? GetMembership(int userId)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.QuerySingleOrDefault<UserClan>(
                "SELECT user_id, clan_id, joined_at FROM public.mod_clans_user_clans WHERE user_id = @UserId", new { UserId = userId });
        }

        public void SetMembership(int userId, int? clanId)
        {
            using var connection = _connectionFactory.CreateConnection();
            if (clanId == null)
            {
                connection.Execute("DELETE FROM public.mod_clans_user_clans WHERE user_id = @UserId", new { UserId = userId });
                return;
            }

            connection.Execute(@"
                INSERT INTO public.mod_clans_user_clans (user_id, clan_id, joined_at)
                VALUES (@UserId, @ClanId, (NOW() AT TIME ZONE 'utc'))
                ON CONFLICT (user_id) DO UPDATE
                    SET clan_id = EXCLUDED.clan_id, joined_at = EXCLUDED.joined_at
                    WHERE public.mod_clans_user_clans.clan_id <> EXCLUDED.clan_id",
                new { UserId = userId, ClanId = clanId.Value });
        }
    }
}
