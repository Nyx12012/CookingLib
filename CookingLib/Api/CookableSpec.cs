using System.Collections.Generic;

namespace CookingLib
{
    /// <summary>Makes an item that is not a stew ingredient into one, such as an ore or a wedge. A field left null means none.</summary>
    public class CookableSpec : IngredientSpec
    {
        /// <summary>Seconds the item rests in hot liquid, unheld, before it dissolves into the stew, 0 to 60.</summary>
        public float SoakSeconds = 2f;

        /// <summary>Physical material names from the dump, such as an oak. Null takes any; otherwise only items of these materials dissolve.</summary>
        public List<string> Materials;

        /// <summary>True pushes the item out of a locked pot, as the game does with a food.</summary>
        public bool PushOutWhenLocked = true;
    }
}
