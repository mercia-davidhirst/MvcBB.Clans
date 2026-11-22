namespace MvcBB.Clans.Api
{
    /// <summary>
    /// This mod's own in-process store for DataProvider=InMemory, mirroring
    /// core's InMemoryStore pattern but scoped to this mod's data. Registered
    /// as a singleton, so every access goes through <see cref="Lock"/>.
    /// </summary>
    public class ClansInMemoryStore
    {
        public object Lock { get; } = new();
        public List<Clan> Clans { get; } = new();
        public Dictionary<int, UserClan> Memberships { get; } = new();
    }
}
