namespace MvcBB.Clans.App
{
    /// <summary>
    /// Caps how often one member can do something in a short window: switching
    /// clans (so nobody hammers the API flipping back and forth) and uploading
    /// icons (so a compromised admin account can't fill the disk). Kept in
    /// memory per App process (mods can't add the host's own rate-limiting
    /// middleware), so counts reset when MvcBB.App restarts.
    /// </summary>
    public class ClanRateLimiter
    {
        public const int MaxClanChanges = 10;
        public const int MaxIconUploads = 30;
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

        public enum LimitedAction { ClanChange, IconUpload }

        private readonly Dictionary<(LimitedAction, int), Queue<DateTime>> _attempts = new();
        private readonly object _lock = new();

        /// <summary>Records an attempt, or returns false if the member is over the limit for that action.</summary>
        public bool TryAcquire(LimitedAction action, int userId)
        {
            var max = action == LimitedAction.ClanChange ? MaxClanChanges : MaxIconUploads;
            var now = DateTime.UtcNow;
            lock (_lock)
            {
                var key = (action, userId);
                if (!_attempts.TryGetValue(key, out var attempts))
                {
                    attempts = new Queue<DateTime>();
                    _attempts[key] = attempts;
                }

                while (attempts.Count > 0 && now - attempts.Peek() >= Window)
                    attempts.Dequeue();

                if (attempts.Count >= max)
                    return false;

                attempts.Enqueue(now);

                // Drop members with no recent attempts so the map can't grow forever.
                if (_attempts.Count > 1000)
                {
                    foreach (var stale in _attempts.Where(kv => kv.Value.Count == 0 || now - kv.Value.Last() >= Window).Select(kv => kv.Key).ToList())
                        _attempts.Remove(stale);
                }

                return true;
            }
        }
    }
}
