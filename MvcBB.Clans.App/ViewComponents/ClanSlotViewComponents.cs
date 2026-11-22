using Microsoft.AspNetCore.Mvc;
using MvcBB.ModKit;

namespace MvcBB.Clans.App.ViewComponents
{
    // View components this mod renders into core's view slots (see
    // ClansAppMod.GetSlotContributions). Core only invokes them while the mod
    // is enabled. Views use explicit paths under Views/Clans/ so they can't
    // collide with another assembly's views.

    /// <summary>layout.head: the mod's stylesheet, served from the DLL at /_mods/clans/.</summary>
    public class ClansStylesheetViewComponent : ViewComponent
    {
        private readonly ClanLookup _lookup;

        public ClansStylesheetViewComponent(ClanLookup lookup)
        {
            _lookup = lookup;
        }

        public IViewComponentResult Invoke(PageSlotContext context) =>
            _lookup.CanView(context)
                ? View("~/Views/Clans/Slots/Stylesheet.cshtml")
                : Content(string.Empty);
    }

    /// <summary>post.author: the author's clan icon and "I am a/an ..." under their name.</summary>
    public class PostAuthorClanViewComponent : ViewComponent
    {
        private readonly ClanLookup _lookup;

        public PostAuthorClanViewComponent(ClanLookup lookup)
        {
            _lookup = lookup;
        }

        public async Task<IViewComponentResult> InvokeAsync(PostSlotContext context)
        {
            if (!_lookup.CanView(context))
                return Content(string.Empty);

            var clan = await _lookup.ForUserAsync(context.AuthorUserId, context.Page.UserIds);
            return clan == null
                ? Content(string.Empty)
                : View("~/Views/Clans/Slots/PostAuthor.cshtml", clan);
        }
    }

    /// <summary>profile.sidebar: the member's clan under their avatar, plus a link to change it on their own profile.</summary>
    public class ProfileClanViewComponent : ViewComponent
    {
        private readonly ClanLookup _lookup;

        public ProfileClanViewComponent(ClanLookup lookup)
        {
            _lookup = lookup;
        }

        public async Task<IViewComponentResult> InvokeAsync(UserSlotContext context)
        {
            if (!_lookup.CanView(context))
                return Content(string.Empty);

            var clan = await _lookup.ForUserAsync(context.UserId, context.Page.UserIds);
            if (clan == null && !context.IsViewerOwnProfile)
                return Content(string.Empty);

            return View("~/Views/Clans/Slots/Profile.cshtml", new ProfileClanModel(clan, context.IsViewerOwnProfile));
        }
    }

    public sealed record ProfileClanModel(ClanDto? Clan, bool IsOwnProfile);

    /// <summary>home.sidebar: a card listing active clans with member counts.</summary>
    public class HomeClansViewComponent : ViewComponent
    {
        private readonly ClanLookup _lookup;

        public HomeClansViewComponent(ClanLookup lookup)
        {
            _lookup = lookup;
        }

        public async Task<IViewComponentResult> InvokeAsync(PageSlotContext context)
        {
            if (!_lookup.CanView(context))
                return Content(string.Empty);

            var clans = (await _lookup.GetActiveClansAsync()).Values
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return clans.Count == 0
                ? Content(string.Empty)
                : View("~/Views/Clans/Slots/Home.cshtml", clans);
        }
    }
}
