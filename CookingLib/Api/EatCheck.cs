using Alta.Inventory;

namespace CookingLib
{
    /// <summary>A player about to start eating something, asked of Edibles.CanEat.</summary>
    public class EatCheck
    {
        readonly Player player;

        internal EatCheck(Player player, UserInfoAndRole info, Item item, LibEdible edible)
        {
            this.player = player;
            UserId = info == null ? 0 : info.Identifier;
            UserName = info == null ? null : info.Username;
            Item = item;
            Edible = edible;
        }

        /// <summary>The player.</summary>
        public Player Player => player == null ? null : player;

        /// <summary>The player's account id.</summary>
        public int UserId { get; }

        /// <summary>The player's account name.</summary>
        public string UserName { get; }

        /// <summary>What they are about to eat.</summary>
        public Item Item { get; }

        /// <summary>The CookingLib change on it, or null for an unchanged game food.</summary>
        public LibEdible Edible { get; }
    }
}
