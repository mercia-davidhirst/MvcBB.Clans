namespace MvcBB.Clans.App
{
    /// <summary>
    /// Picks the article for "I am a/an {Name}". Automatic by default: "an"
    /// when the name starts with a vowel letter (a, e, i, o, u), otherwise "a".
    /// That rule is spelling-based, so an administrator can override it per
    /// clan for the exceptions ("an Honour Guard", "a Unicorn", "a One-Eyed
    /// Jack") or switch the article off entirely ("I am Legion").
    /// </summary>
    public static class ClanArticle
    {
        public const string AutoMode = "auto";
        public const string NoneMode = "none";

        private const string Vowels = "aeiouAEIOU";

        /// <summary>The article to show: "a", "an" or "" (none).</summary>
        public static string Resolve(string name, string? storedArticle)
        {
            if (storedArticle != null)
                return storedArticle;

            var first = name.TrimStart().FirstOrDefault(char.IsLetterOrDigit);
            return first != default && Vowels.Contains(first) ? "an" : "a";
        }

        /// <summary>"I am a Wizard.", "I am an Elf." or "I am Legion." as plain (unencoded) text.</summary>
        public static string Statement(string name, string? storedArticle)
        {
            var article = Resolve(name, storedArticle);
            return article.Length == 0 ? $"I am {name}." : $"I am {article} {name}.";
        }

        /// <summary>Stored value (null, "", "a", "an") to form mode.</summary>
        public static string ToMode(string? storedArticle) => storedArticle switch
        {
            null => AutoMode,
            "" => NoneMode,
            _ => storedArticle
        };

        /// <summary>Form mode to stored value. Anything unexpected means automatic.</summary>
        public static string? FromMode(string? mode) => mode switch
        {
            "a" => "a",
            "an" => "an",
            NoneMode => "",
            _ => null
        };
    }
}
