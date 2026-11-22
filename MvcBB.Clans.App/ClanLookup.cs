using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MvcBB.ModKit;

namespace MvcBB.Clans.App
{
    /// <summary>
    /// What the view slots use to find a member's clan. Scoped, so one
    /// instance serves a whole page: the first post on a thread page loads the
    /// clan of every author on the page in one API call (through core's
    /// <see cref="SlotBatch{TKey, TValue}"/>), and every later post is a
    /// dictionary lookup. Across page views, <see cref="ClansService"/> also
    /// caches each member's clan briefly in memory.
    ///
    /// If the API can't be reached, members simply show no clan on that page
    /// (logged once per request, not once per post).
    /// </summary>
    public class ClanLookup
    {
        private readonly IClansService _clansService;
        private readonly ILogger<ClanLookup> _logger;
        private readonly SlotBatch<string, ClanDto> _batch;
        private Task<IReadOnlyDictionary<int, ClanDto>>? _activeClans;

        public ClanLookup(IClansService clansService, IConfiguration configuration, ILogger<ClanLookup> logger)
        {
            _clansService = clansService;
            _logger = logger;
            ShowToGuests = configuration.GetValue("Clans:ShowToGuests", false);
            _batch = new SlotBatch<string, ClanDto>(LoadManyAsync);
        }

        /// <summary>Clans:ShowToGuests (default false): guests see no clans and no clan widget.</summary>
        public bool ShowToGuests { get; }

        /// <summary>Whether this viewer should see clans at all.</summary>
        public bool CanView(SlotContext context) => ShowToGuests || context.ViewerIsSignedIn;

        /// <summary>The member's active clan, or null.</summary>
        public Task<ClanDto?> ForUserAsync(string userId, IReadOnlyCollection<string>? pageUserIds) =>
            _batch.GetAsync(userId, pageUserIds);

        /// <summary>Active clans by id, loaded once per request (and cached briefly across requests).</summary>
        public Task<IReadOnlyDictionary<int, ClanDto>> GetActiveClansAsync() =>
            _activeClans ??= LoadActiveClansAsync();

        private async Task<IReadOnlyDictionary<int, ClanDto>> LoadActiveClansAsync()
        {
            try
            {
                return (await _clansService.GetActiveClansAsync()).ToDictionary(c => c.Id);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ClanServiceException)
            {
                _logger.LogWarning(ex, "Clans: couldn't load the clan list, so no clans are shown on this page.");
                return new Dictionary<int, ClanDto>();
            }
        }

        private async Task<IReadOnlyDictionary<string, ClanDto>> LoadManyAsync(IReadOnlyCollection<string> userIds, CancellationToken ct)
        {
            var ids = userIds
                .Select(s => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

            var result = new Dictionary<string, ClanDto>();
            if (ids.Count == 0)
                return result;

            var clans = await GetActiveClansAsync();
            if (clans.Count == 0)
                return result;

            IReadOnlyDictionary<int, int> memberships;
            try
            {
                memberships = await _clansService.GetMembershipsAsync(ids, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ClanServiceException)
            {
                _logger.LogWarning(ex, "Clans: couldn't look up members' clans, so none are shown on this page.");
                return result;
            }

            foreach (var (userId, clanId) in memberships)
            {
                if (clans.TryGetValue(clanId, out var clan))
                    result[userId.ToString(CultureInfo.InvariantCulture)] = clan;
            }
            return result;
        }
    }
}
