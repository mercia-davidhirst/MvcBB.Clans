using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MvcBB.Clans.App.ViewComponents;
using MvcBB.ModKit;

namespace MvcBB.Clans.App
{
    public class ClansAppMod : IAppMod
    {
        public const string ModId = "clans";
        public const string HttpClientName = "ClansApi";

        public string Id => ModId;
        public string Name => "Clans";

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var apiBaseUrl = configuration["Api:BaseUrl"]
                ?? throw new InvalidOperationException("Api:BaseUrl is not configured");

            services.AddHttpClient(HttpClientName, client =>
            {
                client.BaseAddress = new Uri(apiBaseUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                // Clans render on every thread page: a hung API should delay a
                // page by seconds, not by HttpClient's 100-second default.
                client.Timeout = TimeSpan.FromSeconds(5);
            });

            services.AddScoped<IClansService, ClansService>();
            services.AddScoped<ClanLookup>();
            services.AddScoped<IClanIconStorage, ClanIconStorageService>();
            services.AddSingleton<ClanRateLimiter>();
        }

        public IEnumerable<ModNavItem> GetNavItems()
        {
            yield return new ModNavItem("Clans", "Clans", "Index", "bi-flag");
        }

        public IEnumerable<ModSlotContribution> GetSlotContributions()
        {
            yield return new ModSlotContribution(CoreSlots.LayoutHead, typeof(ClansStylesheetViewComponent));
            yield return new ModSlotContribution(CoreSlots.PostAuthor, typeof(PostAuthorClanViewComponent));
            yield return new ModSlotContribution(CoreSlots.ProfileSidebar, typeof(ProfileClanViewComponent));
            yield return new ModSlotContribution(CoreSlots.HomeSidebar, typeof(HomeClansViewComponent));
        }
    }
}
