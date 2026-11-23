using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MvcBB.ModKit;

namespace MvcBB.Clans.Api
{
    public class ClansMod : IApiMod
    {
        public string Id => "clans";
        public string Name => "Clans";
        public string Version => "1.0.1";

        // Needs core's view slots, added in ModKit 1.1.0.
        public string MinCoreVersion => "1.1.0";
        public string? MaxCoreVersion => null;

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var dataProvider = configuration["DataProvider"] ?? "InMemory";
            switch (dataProvider)
            {
                case "Sql":
                    services.AddScoped<IClanRepository, SqlClanRepository>();
                    break;
                case "PostgreSql":
                    services.AddScoped<IClanRepository, PostgreSqlClanRepository>();
                    break;
                default:
                    services.AddSingleton<ClansInMemoryStore>();
                    services.AddSingleton<IClanRepository, InMemoryClanRepository>();
                    break;
            }
        }

        public async Task InstallAsync(IModInstallContext context)
        {
            switch (context.DataProvider)
            {
                case "Sql":
                    await InstallSqlServerAsync(context.GetConnection()
                        ?? throw new InvalidOperationException("Expected a SQL Server connection."));
                    break;
                case "PostgreSql":
                    await InstallPostgreSqlAsync(context.GetConnection()
                        ?? throw new InvalidOperationException("Expected a PostgreSQL connection."));
                    break;
                default:
                    // InMemory has no database to install into - ClansInMemoryStore starts empty.
                    break;
            }
        }

        private static async Task InstallSqlServerAsync(System.Data.IDbConnection connection)
        {
            await connection.ExecuteAsync(@"
                IF OBJECT_ID('dbo.Mod_Clans_Clans') IS NULL
                BEGIN
                    CREATE TABLE dbo.Mod_Clans_Clans
                    (
                        Id           INT IDENTITY(1,1) NOT NULL,
                        Name         NVARCHAR(100)      NOT NULL,
                        Article      NVARCHAR(3)        NULL,
                        IconFileName NVARCHAR(100)      NULL,
                        SortOrder    INT                NOT NULL CONSTRAINT DF_Mod_Clans_Clans_SortOrder DEFAULT (0),
                        IsActive     BIT                NOT NULL CONSTRAINT DF_Mod_Clans_Clans_IsActive DEFAULT (1),
                        CreatedAt    DATETIME2(3)       NOT NULL CONSTRAINT DF_Mod_Clans_Clans_CreatedAt DEFAULT (SYSUTCDATETIME()),
                        CONSTRAINT PK_Mod_Clans_Clans PRIMARY KEY CLUSTERED (Id),
                        CONSTRAINT CK_Mod_Clans_Clans_Article CHECK (Article IS NULL OR Article IN (N'', N'a', N'an'))
                    );

                    CREATE UNIQUE NONCLUSTERED INDEX UX_Mod_Clans_Clans_Name ON dbo.Mod_Clans_Clans (Name);
                    CREATE UNIQUE NONCLUSTERED INDEX UX_Mod_Clans_Clans_IconFileName ON dbo.Mod_Clans_Clans (IconFileName) WHERE IconFileName IS NOT NULL;
                END");

            // One row per member for now (UserId is the key). Membership lives
            // in its own table rather than a column on core's Users table, so
            // core's schema is untouched and later features (membership
            // history, competitions, points) can hang off clan and membership rows.
            await connection.ExecuteAsync(@"
                IF OBJECT_ID('dbo.Mod_Clans_UserClans') IS NULL
                BEGIN
                    CREATE TABLE dbo.Mod_Clans_UserClans
                    (
                        UserId   INT          NOT NULL,
                        ClanId   INT          NOT NULL,
                        JoinedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Mod_Clans_UserClans_JoinedAt DEFAULT (SYSUTCDATETIME()),
                        CONSTRAINT PK_Mod_Clans_UserClans PRIMARY KEY CLUSTERED (UserId),
                        CONSTRAINT FK_Mod_Clans_UserClans_User FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
                        CONSTRAINT FK_Mod_Clans_UserClans_Clan FOREIGN KEY (ClanId) REFERENCES dbo.Mod_Clans_Clans (Id)
                    );

                    CREATE NONCLUSTERED INDEX IX_Mod_Clans_UserClans_ClanId ON dbo.Mod_Clans_UserClans (ClanId);
                END");
        }

        private static async Task InstallPostgreSqlAsync(System.Data.IDbConnection connection)
        {
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS public.mod_clans_clans
                (
                    id             INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    name           VARCHAR(100)    NOT NULL,
                    article        VARCHAR(3)      NULL CHECK (article IS NULL OR article IN ('', 'a', 'an')),
                    icon_file_name VARCHAR(100)    NULL,
                    sort_order     INTEGER         NOT NULL DEFAULT 0,
                    is_active      BOOLEAN         NOT NULL DEFAULT TRUE,
                    created_at     TIMESTAMP(3)    NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ux_mod_clans_clans_name ON public.mod_clans_clans (LOWER(name));
                CREATE UNIQUE INDEX IF NOT EXISTS ux_mod_clans_clans_icon_file_name ON public.mod_clans_clans (icon_file_name);");

            // See the SQL Server version above for why membership has its own table.
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS public.mod_clans_user_clans
                (
                    user_id   INTEGER      NOT NULL PRIMARY KEY REFERENCES public.users (id) ON DELETE CASCADE,
                    clan_id   INTEGER      NOT NULL REFERENCES public.mod_clans_clans (id),
                    joined_at TIMESTAMP(3) NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
                );

                CREATE INDEX IF NOT EXISTS ix_mod_clans_user_clans_clan_id ON public.mod_clans_user_clans (clan_id);");
        }
    }
}
