using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Cooking;

namespace CookingLib
{
    internal sealed class StageResults
    {
        readonly Dictionary<StewStage, LiquidRef> stages;

        StageResults(LiquidRef all, Dictionary<StewStage, LiquidRef> stages)
        {
            All = all;
            this.stages = stages;
        }

        internal LiquidRef All { get; }

        internal static StageResults Resolve(string all, Dictionary<StewStage, string> stages)
        {
            LiquidRef every = string.IsNullOrEmpty(all) ? null : LiquidRef.Resolve(all);
            Dictionary<StewStage, LiquidRef> byStage = new Dictionary<StewStage, LiquidRef>();
            foreach (KeyValuePair<StewStage, string> pair in stages ?? new Dictionary<StewStage, string>())
            {
                if (pair.Key != StewStage.Raw && pair.Key != StewStage.Cooked && pair.Key != StewStage.Burnt)
                    throw new ArgumentException("StageResults takes Raw, Cooked and Burnt, not " + pair.Key);
                byStage[pair.Key] = string.IsNullOrEmpty(pair.Value) ? null : LiquidRef.Resolve(pair.Value);
            }
            return every == null && byStage.Count == 0 ? null : new StageResults(every, byStage);
        }

        internal LiquidRef For(StewComponent stew)
        {
            if (stages.Count == 0 || stew == null || stew.Entity == null || stew.Entity.Prefab == null)
                return All;
            StewStages.Entry entry = StewStages.Get(stew.Entity.Prefab.Hash);
            return entry != null && stages.TryGetValue(entry.Stage, out LiquidRef result) ? result : All;
        }

        internal bool Varies => stages.Count > 0;

        internal string Text()
        {
            List<string> parts = new List<string>();
            if (All != null)
                parts.Add(Name(All));
            foreach (KeyValuePair<StewStage, LiquidRef> pair in stages.OrderBy(p => p.Key))
                parts.Add(pair.Key + " " + (pair.Value == null ? "the stew" : Name(pair.Value)));
            return string.Join(", ", parts.ToArray());
        }

        static string Name(LiquidRef liquid) => liquid.Lib != null ? liquid.Lib.Id : liquid.Vanilla.name;
    }
}
