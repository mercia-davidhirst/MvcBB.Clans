using Dapper;
using MvcBB.Shared.Models.User;
using MvcBB.API.SQL;

namespace MvcBB.Clans.Api
{
    public class SqlClanRepository : IClanRepository
    {
        private const string ClanSelect = @"
            SELECT c.Id, c.Name, c.Article, c.IconFileName, c.SortOrder, c.IsActive, c.CreatedAt,
                   (SELECT COUNT(*) FROM dbo.Mod_Clans_UserClans uc INNER JOIN dbo.Users u ON u.Id = uc.UserId
                    WHERE uc.ClanId = c.Id AND u.Role <> 3) AS MemberCount
            FROM dbo.Mod_Clans_Clans c";

        private readonly ISqlConnectionFactory _connectionFactory;

        public SqlClanRepository(ISqlConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public IReadOnlyList<Clan> GetAll(bool includeInactive)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.Query<Clan>(ClanSelect + @"
                WHERE (@IncludeInactive = 1 OR c.IsActive = 1)
                ORDER BY c.SortOrder, c.Name", new { IncludeInactive = includeInactive }).ToList();
        }

        public Clan? GetById(int id)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.QuerySingleOrDefault<Clan>(ClanSelect + " WHERE c.Id = @Id", new { Id = id });
        }

        public bool NameExists(string name, int? exceptId)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM dbo.Mod_Clans_Clans
                WHERE LOWER(Name) = LOWER(@Name) AND (@ExceptId IS NULL OR Id <> @ExceptId)",
                new { Name = name, ExceptId = exceptId }) > 0;
        }

        public bool IconInUse(string iconFileName, int? exceptId)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM dbo.Mod_Clans_Clans
                WHERE IconFileName = @IconFileName AND (@ExceptId IS NULL OR Id <> @ExceptId)",
                new { IconFileName = iconFileName, ExceptId = exceptId }) > 0;
        }

        public Clan Add(Clan clan)
        {
            using var connection = _connectionFactory.CreateConnection();
            var id = connection.QuerySingle<int>(@"
                INSERT INTO dbo.Mod_Clans_Clans (Name, Article, IconFileName, SortOrder, IsActive, CreatedAt)
                OUTPUT inserted.Id
                VALUES (@Name, @Article, @IconFileName, @SortOrder, @IsActive, SYSUTCDATETIME())",
                new { clan.Name, clan.Article, clan.IconFileName, clan.SortOrder, clan.IsActive });

            return GetById(id)!;
        }

        public void Update(Clan clan)
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Execute(@"
                UPDATE dbo.Mod_Clans_Clans
                SET Name = @Name, Article = @Article, IconFileName = @IconFileName, SortOrder = @SortOrder, IsActive = @IsActive
                WHERE Id = @Id",
                new { clan.Id, clan.Name, clan.Article, clan.IconFileName, clan.SortOrder, clan.IsActive });
        }

        public void Remove(int id)
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute("DELETE FROM dbo.Mod_Clans_UserClans WHERE ClanId = @Id", new { Id = id }, transaction);
            connection.Execute("DELETE FROM dbo.Mod_Clans_Clans WHERE Id = @Id", new { Id = id }, transaction);
            transaction.Commit();
        }

        public IReadOnlyDictionary<int, int> GetActiveMemberships(IReadOnlyCollection<int> userIds)
        {
            if (userIds.Count == 0) return new Dictionary<int, int>();

            using var connection = _connectionFactory.CreateConnection();
            return connection.Query<(int UserId, int ClanId)>(@"
                SELECT uc.UserId, uc.ClanId
                FROM dbo.Mod_Clans_UserClans uc
                INNER JOIN dbo.Mod_Clans_Clans c ON c.Id = uc.ClanId
                INNER JOIN dbo.Users u ON u.Id = uc.UserId
                WHERE c.IsActive = 1 AND u.Role <> @DeletedRole AND uc.UserId IN @UserIds",
                new { UserIds = userIds, DeletedRole = (int)UserRole.Deleted }).ToDictionary(r => r.UserId, r => r.ClanId);
        }

        public UserClan? GetMembership(int userId)
        {
            using var connection = _connectionFactory.CreateConnection();
            return connection.QuerySingleOrDefault<UserClan>(
                "SELECT UserId, ClanId, JoinedAt FROM dbo.Mod_Clans_UserClans WHERE UserId = @UserId", new { UserId = userId });
        }

        public void SetMembership(int userId, int? clanId)
        {
            using var connection = _connectionFactory.CreateConnection();
            if (clanId == null)
            {
                connection.Execute("DELETE FROM dbo.Mod_Clans_UserClans WHERE UserId = @UserId", new { UserId = userId });
                return;
            }

            // HOLDLOCK makes the check-then-insert atomic, so two quick
            // submissions can't both try to insert the same member.
            connection.Execute(@"
                MERGE dbo.Mod_Clans_UserClans WITH (HOLDLOCK) AS target
                USING (SELECT @UserId AS UserId, @ClanId AS ClanId) AS source
                    ON target.UserId = source.UserId
                WHEN MATCHED AND target.ClanId <> source.ClanId THEN
                    UPDATE SET ClanId = source.ClanId, JoinedAt = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (UserId, ClanId, JoinedAt) VALUES (source.UserId, source.ClanId, SYSUTCDATETIME());",
                new { UserId = userId, ClanId = clanId.Value });
        }
    }
}
