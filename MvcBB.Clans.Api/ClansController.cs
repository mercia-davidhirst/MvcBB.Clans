using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MvcBB.ModKit;

namespace MvcBB.Clans.Api
{
    [Mod("clans")]
    [ApiController]
    [Route("api/mods/clans")]
    [Authorize]
    public class ClansController : ControllerBase
    {
        /// <summary>Most member ids one membership lookup may ask about.</summary>
        public const int MaxLookupIds = 200;

        private readonly IClanRepository _repository;

        public ClansController(IClanRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Active clans with member counts. Read-only and anonymous, like core's
        /// browsing endpoints: clans are shown next to posts, which guests can
        /// read. Whether guests actually see them is the App's choice
        /// (Clans:ShowToGuests). Inactive clans are Administrator-only.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public ActionResult<IEnumerable<Clan>> GetAll(bool includeInactive = false)
        {
            if (includeInactive && !User.IsInRole("Administrator"))
                return Forbid();

            return Ok(_repository.GetAll(includeInactive));
        }

        [HttpGet("{id:int}")]
        [Authorize(Roles = "Administrator")]
        public ActionResult<Clan> GetById(int id)
        {
            var clan = _repository.GetById(id);
            return clan == null ? NotFound(new { message = "Clan not found" }) : Ok(clan);
        }

        [HttpPost]
        [Authorize(Roles = "Administrator")]
        public ActionResult<Clan> Create(SaveClanRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var name = request.Name.Trim();
            if (name.Length == 0) return BadRequest(new { message = "Please give the clan a name." });
            if (_repository.NameExists(name, null))
                return Conflict(new { message = "Another clan already has that name." });
            if (request.IconFileName != null && _repository.IconInUse(request.IconFileName, null))
                return Conflict(new { message = "That icon file is already in use." });

            var created = _repository.Add(new Clan
            {
                Name = name,
                Article = request.Article,
                IconFileName = request.IconFileName,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive
            });
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        /// <summary>
        /// Replaces a clan's details. Returns the icon file the clan no longer
        /// uses (if any), so the App can remove it from disk.
        /// </summary>
        [HttpPut("{id:int}")]
        [Authorize(Roles = "Administrator")]
        public IActionResult Update(int id, SaveClanRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existing = _repository.GetById(id);
            if (existing == null) return NotFound(new { message = "Clan not found" });

            var name = request.Name.Trim();
            if (name.Length == 0) return BadRequest(new { message = "Please give the clan a name." });
            if (_repository.NameExists(name, id))
                return Conflict(new { message = "Another clan already has that name." });
            if (request.IconFileName != null && _repository.IconInUse(request.IconFileName, id))
                return Conflict(new { message = "That icon file is already in use." });

            _repository.Update(new Clan
            {
                Id = id,
                Name = name,
                Article = request.Article,
                IconFileName = request.IconFileName,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive
            });

            var replacedIcon = existing.IconFileName != null && existing.IconFileName != request.IconFileName
                ? existing.IconFileName
                : null;
            return Ok(new { removedIconFileName = replacedIcon });
        }

        /// <summary>
        /// Deletes a clan. A clan with members is only deleted when
        /// <paramref name="removeMembers"/> confirms that their memberships go
        /// too; otherwise this answers 409 with the member count. Deactivating
        /// the clan instead keeps its memberships.
        /// </summary>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Administrator")]
        public IActionResult Delete(int id, bool removeMembers = false)
        {
            var clan = _repository.GetById(id);
            if (clan == null) return NotFound(new { message = "Clan not found" });

            if (clan.MemberCount > 0 && !removeMembers)
            {
                return Conflict(new
                {
                    message = $"{clan.Name} still has {clan.MemberCount} {(clan.MemberCount == 1 ? "member" : "members")}. Confirm removing their memberships, or deactivate the clan instead.",
                    memberCount = clan.MemberCount
                });
            }

            _repository.Remove(id);
            return Ok(new { removedIconFileName = clan.IconFileName });
        }

        /// <summary>
        /// Bulk lookup for the App's view slots: member id to clan id, for the
        /// given members who belong to an active clan. Members without one are
        /// left out. Capped at <see cref="MaxLookupIds"/> ids per call.
        /// </summary>
        [HttpGet("members")]
        [AllowAnonymous]
        public ActionResult<IDictionary<string, int>> GetMemberships(string? userIds)
        {
            var ids = (userIds ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (ids.Count > MaxLookupIds)
                return BadRequest(new { message = $"Ask about at most {MaxLookupIds} members at a time." });

            var memberships = _repository.GetActiveMemberships(ids);
            return Ok(memberships.ToDictionary(m => m.Key.ToString(CultureInfo.InvariantCulture), m => m.Value));
        }

        /// <summary>The signed-in member's own clan (active or not), or a null clanId.</summary>
        [HttpGet("me")]
        public IActionResult GetMine()
        {
            if (CurrentUserId is not int userId) return Unauthorized();
            var membership = _repository.GetMembership(userId);
            return Ok(new { clanId = membership?.ClanId, joinedAt = membership?.JoinedAt });
        }

        /// <summary>Joins an active clan (replacing any current one), or leaves with a null clanId.</summary>
        [HttpPut("me")]
        public IActionResult SetMine(SetMyClanRequest request)
        {
            if (CurrentUserId is not int userId) return Unauthorized();

            if (request.ClanId is int clanId)
            {
                var clan = _repository.GetById(clanId);
                if (clan == null || !clan.IsActive)
                    return BadRequest(new { message = "Please choose one of the clans listed." });
            }

            _repository.SetMembership(userId, request.ClanId);
            return NoContent();
        }

        private int? CurrentUserId =>
            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    }
}
