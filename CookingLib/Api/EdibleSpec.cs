using System.Collections.Generic;

namespace CookingLib
{
    /// <summary>What a food gives when eaten: a change to one of the game's foods, or an item that is not food made edible.</summary>
    public class EdibleSpec
    {
        /// <summary>Unique id written as author.thing.</summary>
        public string Id;

        /// <summary>The item, by the game's item or prefab name or number, copied from the dump. Each stage of a food is its own food.</summary>
        public string Item;

        /// <summary>What eating it gives, at most 3 that are not 0. On a game food it replaces the list, Feed apart: Feed stays the game's and is kept, and its food page shows them. An item made edible may carry Feed and has no page.</summary>
        public List<EffectSpec> Effects = new List<EffectSpec>();

        /// <summary>Made edible only: how many bites it takes, 1 to 20. 0 copies LikeFood.</summary>
        public int Bites;

        /// <summary>Made edible only: seconds per bite, 0.05 to 10, all bites together at most 10. 0 copies LikeFood.</summary>
        public float BiteSeconds;

        /// <summary>Made edible only: the game food whose bites, crunch and crumbs it copies.</summary>
        public string LikeFood = "Apple Full Ripe";
    }
}
