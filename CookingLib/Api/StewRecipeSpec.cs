using System.Collections.Generic;
using UnityEngine;

namespace CookingLib
{
    /// <summary>Everything a mod says about a new stew recipe.</summary>
    public class StewRecipeSpec
    {
        /// <summary>Unique id written as author.thing.</summary>
        public string Id;

        /// <summary>A vanilla stew recipe or stew prefab whose look and name the stew borrows, copied from the dump.</summary>
        public string Look;

        /// <summary>Liquids the pot may hold: registered ids or vanilla liquid names. Assign a new list to replace the default Water.</summary>
        public List<string> BaseLiquids = new List<string> { "Water" };

        /// <summary>One slot per ingredient. The usual pot checks recipes at exactly 4 ingredients.</summary>
        public List<SlotSpec> Slots = new List<SlotSpec>();

        /// <summary>Up to 3 effects the stew gains on top of its ingredients.</summary>
        public List<EffectSpec> ExtraEffects = new List<EffectSpec>();

        /// <summary>Multipliers on the stew's whole total for an effect. Like extra effects, a listed multiplier is also scaled by the stew's stage. Effects not listed are left as they are.</summary>
        public List<EffectSpec> EffectModifiers = new List<EffectSpec>();

        /// <summary>Optional colour in place of the borrowed stew's own. Every channel from 0 to 1.</summary>
        public Color? Tint;

        /// <summary>The colour players should see at every stage, as #RRGGBB or a name. CookingLib works out the tint for each stage's look, so the stew keeps one colour from Raw to Burnt as far as each look allows. Use it or Tint, not both.</summary>
        public string Shade;

        /// <summary>True keeps this recipe's own colour on a base liquid that sets ColorsStews.</summary>
        public bool IgnoresDyes;

        /// <summary>Optional registered id or vanilla liquid name the pot holds instead of the stew.</summary>
        public string ResultLiquid;

        /// <summary>A result per stage, overriding ResultLiquid at that stage: Raw, Cooked or Burnt to a registered id or game liquid name, or to an empty string to keep the stew at that stage.</summary>
        public Dictionary<StewStage, string> StageResults = new Dictionary<StewStage, string>();
    }
}
