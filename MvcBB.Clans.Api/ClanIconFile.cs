namespace MvcBB.Clans.Api
{
    /// <summary>The only icon file name shape the mod stores: what the App's
    /// ClanIconStorageService generates for a saved upload. The App deletes the
    /// file with this name when an icon is replaced or a clan removed, so a
    /// client-chosen name such as "../appsettings.json" must never be stored.</summary>
    public static class ClanIconFile
    {
        public const string Pattern =
            @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.(png|jpg|gif|webp)$";
    }
}
