namespace CookingLib
{
    /// <summary>What a custom effect does.</summary>
    public enum EffectKind
    {
        /// <summary>A copy of a game buff such as Speed: adds to a stat for a while.</summary>
        Buff,

        /// <summary>A copy of a game stat change such as Heal: adds its strength to a stat at once.</summary>
        Change,

        /// <summary>A copy of a game teleport: strength is the chance.</summary>
        Teleport,

        /// <summary>Does nothing on its own. A marker that drink and eat events can look for.</summary>
        Tag,

        /// <summary>Runs your handler on the server.</summary>
        Code
    }
}
