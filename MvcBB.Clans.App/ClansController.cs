using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MvcBB.ModKit;

namespace MvcBB.Clans.App
{
    [Mod(ClansAppMod.ModId)]
    [Authorize]
    public class ClansController : Controller
    {
        private readonly IClansService _clansService;
        private readonly IClanIconStorage _iconStorage;
        private readonly ClanRateLimiter _rateLimiter;
        private readonly ClanLookup _lookup;

        public ClansController(IClansService clansService, IClanIconStorage iconStorage, ClanRateLimiter rateLimiter, ClanLookup lookup)
        {
            _clansService = clansService;
            _iconStorage = iconStorage;
            _rateLimiter = rateLimiter;
            _lookup = lookup;
        }

        /// <summary>Every active clan with its member count, and the viewer's own clan.</summary>
        public async Task<IActionResult> Index()
        {
            try
            {
                ViewBag.MyClanId = await _clansService.GetMyClanIdAsync();
                return View(await _clansService.GetActiveClansAsync());
            }
            catch (ClanServiceException ex)
            {
                TempData["Error"] = ex.Message;
                ViewBag.MyClanId = null;
                return View(Array.Empty<ClanDto>());
            }
        }

        /// <summary>
        /// Serves a stored clan icon. Icons live outside wwwroot, so this is the
        /// only way to open one. Members only unless Clans:ShowToGuests is on
        /// (then guests see clans on posts, so they need the icons too).
        /// </summary>
        [HttpGet("/images/clans/{fileName}")]
        [AllowAnonymous]
        public IActionResult IconFile(string fileName)
        {
            if (!_lookup.ShowToGuests && User.Identity?.IsAuthenticated != true)
                return Challenge();

            var filePath = _iconStorage.GetIconFilePath(fileName);
            if (filePath == null) return NotFound();

            Response.Headers["X-Content-Type-Options"] = "nosniff";
            // Stored names are random and never reused, so a day's browser
            // caching is safe. "private": shared caches/proxies must not keep
            // members-only files.
            Response.Headers["Cache-Control"] = "private, max-age=86400";
            return PhysicalFile(filePath, ClanIconStorageService.ContentTypeFor(filePath), enableRangeProcessing: false);
        }

        [HttpGet]
        public async Task<IActionResult> Choose()
        {
            try
            {
                ViewBag.MyClanId = await _clansService.GetMyClanIdAsync();
                return View(await _clansService.GetActiveClansAsync());
            }
            catch (ClanServiceException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Choose(int? clanId)
        {
            try
            {
                if (clanId <= 0) clanId = null;

                if (CurrentUserId is not int userId || !_rateLimiter.TryAcquire(ClanRateLimiter.LimitedAction.ClanChange, userId))
                    throw new ClanServiceException("You've changed clans a lot in a short time. Please wait a few minutes and try again.");

                // The API checks this too; checking here gives a friendlier message.
                if (clanId is int id && (await _clansService.GetActiveClansAsync()).All(c => c.Id != id))
                    throw new ClanServiceException("Please choose one of the clans listed.");

                await _clansService.SetMyClanAsync(clanId);
                TempData["Success"] = clanId == null ? "You've left your clan." : "Your clan has been updated.";
                return RedirectToAction(nameof(Index));
            }
            catch (ClanServiceException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Choose));
            }
        }

        // ---- Administration ----

        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> Manage() => View(await _clansService.GetAllClansAsync());

        [HttpGet]
        [Authorize(Roles = "Administrator")]
        public IActionResult Create() => View(new ClanFormModel());

        [HttpPost]
        [Authorize(Roles = "Administrator")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(ClanIconStorageService.MaxFileSizeBytes + 64 * 1024)]
        public async Task<IActionResult> Create(ClanFormModel form)
        {
            form.Name = form.Name?.Trim() ?? string.Empty;
            if (!ModelState.IsValid) return View(form);

            string? savedIcon = null;
            try
            {
                if (form.Icon != null)
                    savedIcon = await SaveIconAsync(form.Icon);

                await _clansService.CreateClanAsync(form.Name, ClanArticle.FromMode(form.ArticleMode), savedIcon, form.SortOrder, form.IsActive);
                TempData["Success"] = $"Added {form.Name}.";
                return RedirectToAction(nameof(Manage));
            }
            catch (ClanServiceException ex)
            {
                // The API didn't record it, so nothing points at this file.
                _iconStorage.DeleteIcon(savedIcon);
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(form);
            }
        }

        [HttpGet]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> Edit(int id)
        {
            var clan = await _clansService.GetClanAsync(id);
            if (clan == null) return NotFound();

            return View(new ClanFormModel
            {
                Id = clan.Id,
                Name = clan.Name,
                ArticleMode = ClanArticle.ToMode(clan.Article),
                SortOrder = clan.SortOrder,
                IsActive = clan.IsActive,
                CurrentIconUrl = clan.IconUrl
            });
        }

        [HttpPost]
        [Authorize(Roles = "Administrator")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(ClanIconStorageService.MaxFileSizeBytes + 64 * 1024)]
        public async Task<IActionResult> Edit(int id, ClanFormModel form)
        {
            var existing = await _clansService.GetClanAsync(id);
            if (existing == null) return NotFound();

            form.Id = id;
            form.Name = form.Name?.Trim() ?? string.Empty;
            form.CurrentIconUrl = existing.IconUrl;
            if (!ModelState.IsValid) return View(form);

            string? savedIcon = null;
            try
            {
                if (form.Icon != null)
                    savedIcon = await SaveIconAsync(form.Icon);

                var iconFileName = savedIcon ?? (form.RemoveIcon ? null : existing.IconFileName);
                var removedIcon = await _clansService.UpdateClanAsync(id, form.Name, ClanArticle.FromMode(form.ArticleMode), iconFileName, form.SortOrder, form.IsActive);
                _iconStorage.DeleteIcon(removedIcon);

                TempData["Success"] = $"Saved {form.Name}.";
                return RedirectToAction(nameof(Manage));
            }
            catch (ClanServiceException ex)
            {
                _iconStorage.DeleteIcon(savedIcon);
                ModelState.AddModelError(string.Empty, ex.Message);
                return View(form);
            }
        }

        [HttpGet]
        [Authorize(Roles = "Administrator")]
        public async Task<IActionResult> Delete(int id)
        {
            var clan = await _clansService.GetClanAsync(id);
            return clan == null ? NotFound() : View(clan);
        }

        /// <summary>
        /// Deletes a clan. If it has members, <paramref name="removeMembers"/>
        /// must confirm that their memberships are removed too; the API refuses
        /// otherwise.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Administrator")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, bool removeMembers)
        {
            try
            {
                var removedIcon = await _clansService.DeleteClanAsync(id, removeMembers);
                _iconStorage.DeleteIcon(removedIcon);
                TempData["Success"] = "Clan deleted.";
                return RedirectToAction(nameof(Manage));
            }
            catch (ClanServiceException ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Delete), new { id });
            }
        }

        private async Task<string> SaveIconAsync(Microsoft.AspNetCore.Http.IFormFile icon)
        {
            if (CurrentUserId is not int userId || !_rateLimiter.TryAcquire(ClanRateLimiter.LimitedAction.IconUpload, userId))
                throw new ClanServiceException("You've uploaded a lot of icons in a short time. Please wait a few minutes and try again.");

            return await _iconStorage.SaveIconAsync(icon);
        }

        private int? CurrentUserId =>
            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    }
}
