using Alta.Alchemy;

namespace CookingLib
{
    /// <summary>One frame of a code effect landing on one target. A copy taken when it happened.</summary>
    public class EffectHit
    {
        readonly Player player;

        internal EffectHit(LibEffect effect, EffectHandler target, Player player, UserInfoAndRole info)
        {
            Effect = effect;
            Target = target;
            this.player = player;
            UserId = info == null ? 0 : info.Identifier;
            UserName = info == null ? null : info.Username;
        }

        /// <summary>The effect that landed.</summary>
        public LibEffect Effect { get; }

        /// <summary>The strengths of this frame's hits added up. May be negative, as when a player leaves an effect area.</summary>
        public float Strength { get; internal set; }

        /// <summary>How many hits were merged into this one.</summary>
        public int Hits { get; internal set; }

        /// <summary>Who it landed on. May be gone by the time the handler runs.</summary>
        public EffectHandler Target { get; }

        /// <summary>The player it landed on, or null for a creature or a player who left.</summary>
        public Player Player => player == null ? null : player;

        /// <summary>The player's user id, 0 for a creature.</summary>
        public int UserId { get; }

        /// <summary>The player's name, null for a creature.</summary>
        public string UserName { get; }
    }
}
