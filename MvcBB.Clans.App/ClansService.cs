using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace MvcBB.Clans.App
{
    /// <summary>
    /// Client for MvcBB.Clans.Api's endpoints. Calls attach the signed-in
    /// member's own token whenever there is one, never a shared or service
    /// token. Only the two read-only lookups used by the view slots may go out
    /// without one (they're anonymous on the API side, so guest pages can show
    /// clans when Clans:ShowToGuests is on).
    /// </summary>
    public class ClansService : IClansService
    {
        /// <summary>Must match the API's ClansController.MaxLookupIds.</summary>
        private const int LookupChunkSize = 200;

        private const string ActiveClansCacheKey = "mods:clans:active";
        private const string MembershipCacheKeyPrefix = "mods:clans:member:";

        /// <summary>How long the clan list (with member counts) is reused.</summary>
        private static readonly TimeSpan ClanListCacheDuration = TimeSpan.FromSeconds(60);

        /// <summary>How long one member's clan is reused across page views.</summary>
        private static readonly TimeSpan MembershipCacheDuration = TimeSpan.FromSeconds(60);

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly HttpClient _httpClient;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMemoryCache _cache;

        public ClansService(IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor, IMemoryCache cache)
        {
            _httpClient = httpClientFactory.CreateClient(ClansAppMod.HttpClientName);
            _httpContextAccessor = httpContextAccessor;
            _cache = cache;
        }

        public async Task<IReadOnlyList<ClanDto>> GetActiveClansAsync()
        {
            if (_cache.TryGetValue(ActiveClansCacheKey, out IReadOnlyList<ClanDto>? cached) && cached != null)
                return cached;

            AttachTokenIfSignedIn();
            var response = await _httpClient.GetAsync("api/mods/clans");
            await EnsureSuccessAsync(response);
            IReadOnlyList<ClanDto> clans = (await DeserializeAsync<List<ClanDto>>(response)) ?? new List<ClanDto>();

            _cache.Set(ActiveClansCacheKey, clans, ClanListCacheDuration);
            return clans;
        }

        public async Task<IReadOnlyList<ClanDto>> GetAllClansAsync()
        {
            Authorize();
            var response = await _httpClient.GetAsync("api/mods/clans?includeInactive=true");
            await EnsureSuccessAsync(response);
            return (await DeserializeAsync<List<ClanDto>>(response)) ?? new List<ClanDto>();
        }

        public async Task<ClanDto?> GetClanAsync(int id)
        {
            Authorize();
            var response = await _httpClient.GetAsync($"api/mods/clans/{id}");
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            await EnsureSuccessAsync(response);
            return await DeserializeAsync<ClanDto>(response);
        }

        public async Task<ClanDto> CreateClanAsync(string name, string? article, string? iconFileName, int sortOrder, bool isActive)
        {
            Authorize();
            var response = await SendJsonAsync(HttpMethod.Post, "api/mods/clans", new { name, article, iconFileName, sortOrder, isActive });
            await EnsureSuccessAsync(response);
            InvalidateClanCache();
            return (await DeserializeAsync<ClanDto>(response))!;
        }

        public async Task<string?> UpdateClanAsync(int id, string name, string? article, string? iconFileName, int sortOrder, bool isActive)
        {
            Authorize();
            var response = await SendJsonAsync(HttpMethod.Put, $"api/mods/clans/{id}", new { name, article, iconFileName, sortOrder, isActive });
            await EnsureSuccessAsync(response);
            InvalidateClanCache();
            return await ReadRemovedIconAsync(response);
        }

        public async Task<string?> DeleteClanAsync(int id, bool removeMembers)
        {
            Authorize();
            var response = await _httpClient.DeleteAsync($"api/mods/clans/{id}?removeMembers={(removeMembers ? "true" : "false")}");
            await EnsureSuccessAsync(response);
            InvalidateClanCache();
            return await ReadRemovedIconAsync(response);
        }

        public async Task<IReadOnlyDictionary<int, int>> GetMembershipsAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
        {
            var result = new Dictionary<int, int>();
            var missing = new List<int>();
            foreach (var userId in userIds.Where(id => id > 0).Distinct())
            {
                // 0 is cached for "no clan", so members without one aren't asked about again.
                if (_cache.TryGetValue(MembershipCacheKeyPrefix + userId, out int clanId))
                {
                    if (clanId > 0) result[userId] = clanId;
                }
                else
                {
                    missing.Add(userId);
                }
            }

            if (missing.Count == 0)
                return result;

            AttachTokenIfSignedIn();
            foreach (var chunk in missing.Chunk(LookupChunkSize))
            {
                var ids = string.Join(",", chunk.Select(id => id.ToString(CultureInfo.InvariantCulture)));
                var response = await _httpClient.GetAsync($"api/mods/clans/members?userIds={ids}", ct);
                await EnsureSuccessAsync(response);
                var found = (await DeserializeAsync<Dictionary<string, int>>(response)) ?? new Dictionary<string, int>();

                foreach (var userId in chunk)
                {
                    var clanId = found.TryGetValue(userId.ToString(CultureInfo.InvariantCulture), out var id) ? id : 0;
                    _cache.Set(MembershipCacheKeyPrefix + userId, clanId, MembershipCacheDuration);
                    if (clanId > 0) result[userId] = clanId;
                }
            }

            return result;
        }

        public async Task<int?> GetMyClanIdAsync()
        {
            Authorize();
            var response = await _httpClient.GetAsync("api/mods/clans/me");
            await EnsureSuccessAsync(response);
            var result = await DeserializeAsync<JsonElement>(response);
            return result.ValueKind == JsonValueKind.Object
                   && result.TryGetProperty("clanId", out var clanId)
                   && clanId.ValueKind == JsonValueKind.Number
                ? clanId.GetInt32()
                : null;
        }

        public async Task SetMyClanAsync(int? clanId)
        {
            Authorize();
            var response = await SendJsonAsync(HttpMethod.Put, "api/mods/clans/me", new { clanId });
            await EnsureSuccessAsync(response);

            // Show the change straight away on this App instance: the member's
            // own posts and profile, and the member counts.
            if (CurrentUserId is int userId)
                _cache.Remove(MembershipCacheKeyPrefix + userId);
            InvalidateClanCache();
        }

        public void InvalidateClanCache() => _cache.Remove(ActiveClansCacheKey);

        private int? CurrentUserId =>
            int.TryParse(_httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

        private static async Task<string?> ReadRemovedIconAsync(HttpResponseMessage response)
        {
            var result = await DeserializeAsync<JsonElement>(response);
            return result.ValueKind == JsonValueKind.Object
                   && result.TryGetProperty("removedIconFileName", out var icon)
                   && icon.ValueKind == JsonValueKind.String
                ? icon.GetString()
                : null;
        }

        private void Authorize()
        {
            var token = _httpContextAccessor.HttpContext?.User.FindFirst("api_token")?.Value
                ?? throw new ClanServiceException("Your session has expired. Please log in again.");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        private void AttachTokenIfSignedIn()
        {
            var token = _httpContextAccessor.HttpContext?.User.FindFirst("api_token")?.Value;
            _httpClient.DefaultRequestHeaders.Authorization = token == null ? null : new AuthenticationHeaderValue("Bearer", token);
        }

        private Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, object payload)
        {
            var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            return _httpClient.SendAsync(request);
        }

        private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body)) return default;
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new ClanServiceException("Your session has expired. Please log in again.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new ClanServiceException("You don't have permission to do that.");

            var body = await response.Content.ReadAsStringAsync();
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("message", out var message))
                    throw new ClanServiceException(message.GetString() ?? "The clans service returned an error.");
            }
            catch (JsonException)
            {
                // Not a JSON error body - fall through to the generic exception below.
            }

            response.EnsureSuccessStatusCode();
        }
    }
}
