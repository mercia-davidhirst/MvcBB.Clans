namespace MvcBB.Clans.App
{
    /// <summary>User-facing error for this mod - mirrors MvcBB.App's own ServiceException,
    /// which this mod can't reference directly (mods only depend on MvcBB.ModKit).</summary>
    public class ClanServiceException : Exception
    {
        public ClanServiceException(string message) : base(message) { }
    }
}
