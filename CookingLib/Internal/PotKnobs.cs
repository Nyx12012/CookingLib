using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Alta;
using Alta.Alchemy;
using Alta.Cooking;
using Alta.Global;
using Alta.Inventory;
using Alta.Liquid;
using Alta.Networking;

namespace CookingLib
{
    internal static class PotKnobs
    {
        const uint FullnessHash = 34710;
        const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        static readonly FieldInfo ingredientsField = typeof(PotDefinition).GetField("minNumberOfIngredients", PrivateInstance);
        static readonly FieldInfo garnishesField = typeof(PotDefinition).GetField("numberOfGarnishes", PrivateInstance);
        static readonly FieldInfo temperatureField = typeof(CookingSettings).GetField("minimumStewingTemperature", PrivateInstance);
        static readonly FieldInfo globalField = typeof(CookingSettings).GetField("globalStewEffects", PrivateInstance);

        static List<Pot> pots;
        static bool written;

        internal static string Error { get; private set; }

        internal static bool Built => pots != null;

        internal static IReadOnlyList<Pot> Definitions => pots ?? new List<Pot>();

        internal static float GameTemperature { get; private set; }

        internal static EffectDefinitionApplication[] GameGlobal { get; private set; }

        internal static CookingSettings Settings => GlobalSettings<CookingSettings>.Instance;

        internal static float LiveTemperature => Settings.MinimumStewingTemperature;

        internal static EffectDefinitionApplication[] LiveGlobal => Settings.GlobalStewEffects ?? new EffectDefinitionApplication[0];

        internal static bool Ready()
        {
            if (pots != null)
                return true;
            if (Error != null)
                return false;

            try
            {
                if (ingredientsField == null || garnishesField == null || temperatureField == null || globalField == null)
                    throw new InvalidOperationException("the game's pot fields were not found");
                CookingSettings settings = Settings;
                if (settings == null)
                    throw new InvalidOperationException("the game's cooking settings are not loaded");

                Stopwatch watch = Stopwatch.StartNew();
                Dictionary<PotDefinition, Pot> found = new Dictionary<PotDefinition, Pot>();
                foreach (NetworkPrefab prefab in PrefabWalk.All())
                {
                    foreach (CookingPot pot in prefab.GetComponentsInChildren<CookingPot>(true))
                    {
                        PotDefinition definition = pot.Definition;
                        if (definition == null)
                            continue;
                        if (!found.TryGetValue(definition, out Pot entry))
                            found[definition] = entry = new Pot(definition);
                        if (!entry.Prefabs.Contains(prefab))
                            entry.Prefabs.Add(prefab);
                    }
                }

                GameTemperature = settings.MinimumStewingTemperature;
                GameGlobal = settings.GlobalStewEffects ?? new EffectDefinitionApplication[0];
                pots = found.Values.ToList();
                Log.Info("pot rules: " + pots.Count + " pot definitions on " + pots.Sum(p => p.Prefabs.Count) + " prefabs, found in " + watch.ElapsedMilliseconds + " ms");
                if (pots.Count == 0)
                    Log.Warn("pot rules: no prefab carries a cooking pot, so pot rules reach no pot");
                return true;
            }
            catch (Exception e)
            {
                Error = e is InvalidOperationException ? e.Message : e.ToString();
                Log.Error("pot rules can't reach the game's pots: " + Error);
                return false;
            }
        }

        internal static bool IsKnown(PotDefinition definition) => pots != null && pots.Any(p => p.Definition == definition);

        internal static bool Write(int? ingredients, int? garnishes, float? temperature, EffectDefinitionApplication[] global)
        {
            if (!Ready())
                return false;
            bool any = ingredients.HasValue || garnishes.HasValue || temperature.HasValue || global != null;
            if (!any && !written)
                return true;

            try
            {
                foreach (Pot pot in pots)
                {
                    ingredientsField.SetValue(pot.Definition, ingredients ?? pot.GameIngredients);
                    garnishesField.SetValue(pot.Definition, garnishes ?? pot.GameGarnishes);
                }
                CookingSettings settings = Settings;
                temperatureField.SetValue(settings, temperature ?? GameTemperature);
                globalField.SetValue(settings, global != null ? global.ToArray() : GameGlobal);
                written = any;
                return true;
            }
            catch (Exception e)
            {
                Safe.Fail("pot rules write", e);
                return false;
            }
        }

        internal static float WorstFeed(bool game)
        {
            if (pots == null || pots.Count == 0)
                return Gate(game ? GameGlobal : LiveGlobal);

            int count = pots.Max(p => game ? p.GameIngredients : p.Ingredients);
            int garnishCount = pots.Max(p => game ? p.GameGarnishes : p.Garnishes);
            List<StewComponent> stews = StewStages.All.Select(e => e.Stew).Where(s => s != null).ToList();
            float ingredientTimes = stews.Count == 0 ? 1f : Math.Max(0f, stews.Max(s => s.BaseEffectsMultiplier));
            float liquidTimes = stews.Count == 0 ? 1f : Math.Max(0f, stews.Max(s => s.BaseLiquidEffectsMultiplier));
            float extraTimes = stews.Count == 0 ? 1f : Math.Max(0f, stews.Max(s => s.ExtraEffectsMultiplier));

            List<CookableItem> cookables = HashedGeneralValue<Item>.All.Where(i => i != null).Select(i => i.GetComponent<CookableItem>()).Where(c => c != null).ToList();
            List<CookingGarnish> garnishes = cookables.OfType<CookingGarnish>().ToList();
            float ingredient = Best(cookables.Where(c => c.GetType() == typeof(CookableItem)).Select(c => Gate(c.Effects)));
            float garnishAdds = Best(garnishes.Select(g => Gate(g.Effects)));
            float garnishTimes = Math.Max(1f, Best(garnishes.Select(g => Multiplier(g.EffectMultipliers))));
            float liquid = Best(Liquids.GameLiquids().Where(l => l.Data != null).Select(l => l.Data).Concat(Liquids.All.Where(l => l.Error == null).Select(l => l.Data)).Select(d => Gate(d.Effects)));
            List<StewRecipe> recipes = HashedGeneralValue<StewRecipe>.All.Where(r => r != null).ToList();
            float extra = Best(recipes.Select(r => Gate(r.ExtraEffects)));
            float modifier = Math.Max(1f, Best(recipes.Select(r => Multiplier(r.EffectModifiers))) * extraTimes);

            float sum = Gate(game ? GameGlobal : LiveGlobal) + count * ingredient * ingredientTimes + liquid * liquidTimes + extra * extraTimes + garnishCount * garnishAdds;
            return sum * (float)Math.Pow(garnishTimes, garnishCount) * modifier;
        }

        internal static float Gate(IEnumerable<EffectDefinitionApplication> effects) =>
            (effects ?? Enumerable.Empty<EffectDefinitionApplication>()).Where(e => e != null && IsFullness(e.EffectToModify)).Sum(e => e.StrengthMultiplier);

        static float Multiplier(IEnumerable<EffectDefinitionApplication> effects) =>
            Best((effects ?? Enumerable.Empty<EffectDefinitionApplication>()).Where(e => e != null && IsFullness(e.EffectToModify)).Select(e => e.StrengthMultiplier));

        static bool IsFullness(EffectDefinition effect) => effect is StatChangeEffect stat && stat.StatToAffect != null && stat.StatToAffect.Hash == FullnessHash;

        static float Best(IEnumerable<float> values)
        {
            float best = 0f;
            foreach (float value in values)
            {
                if (value > best)
                    best = value;
            }
            return best;
        }

        internal sealed class Pot
        {
            internal Pot(PotDefinition definition)
            {
                Definition = definition;
                GameIngredients = definition.MinNumberOfIngredients;
                GameGarnishes = definition.NumberOfGarnishes;
            }

            internal PotDefinition Definition { get; }

            internal List<NetworkPrefab> Prefabs { get; } = new List<NetworkPrefab>();

            internal int GameIngredients { get; }

            internal int GameGarnishes { get; }

            internal int Ingredients => Definition.MinNumberOfIngredients;

            internal int Garnishes => Definition.NumberOfGarnishes;
        }
    }
}
