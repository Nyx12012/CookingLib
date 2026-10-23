using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Alta;
using Alta.Alchemy;
using Alta.Cooking;
using Alta.Inventory;
using Alta.Liquid;

namespace CookingLib
{
    internal static class VanillaReport
    {
        internal static void Status(StringBuilder text)
        {
            text.Append("vanilla recipes: ").Append(VanillaRecipes.All.Count).Append('\n');
            foreach (LibVanillaRecipe edit in VanillaRecipes.All)
            {
                string target = edit.Recipe != null ? edit.Recipe.name : edit.Spec?.Recipe;
                text.Append("  ").Append(edit.Id).Append("  ").Append(target).Append(": ").Append(RecipeState(edit)).Append('\n');
            }

            text.Append("vanilla ingredients: ").Append(VanillaIngredients.All.Count).Append('\n');
            foreach (LibIngredient entry in VanillaIngredients.All)
            {
                string target = entry.Item != null ? entry.Item.name : entry.Spec?.Item;
                text.Append("  ").Append(entry.Id).Append("  ").Append(target).Append(": ").Append(IngredientState(entry)).Append('\n');
            }

            List<LibEdible> foods = Edibles.All.ToList();
            text.Append("edibles: ").Append(foods.Count(e => e.Error == null && e.IsReady)).Append(" ready (")
                .Append(foods.Where(e => e.Error == null && e.IsReady).Select(e => e.PrefabHash).Distinct().Count()).Append(" foods changed), ")
                .Append(foods.Count(e => e.Error != null)).Append(" refused, swept ").Append(Edibles.Swept)
                .Append(Main.IsServer && !ServerPatches.PrefabMapOn ? ", the prefab map patch failed to apply so foods change after Setup" : "").Append('\n');
            foreach (LibEdible entry in foods)
            {
                string target = entry.Item != null ? entry.Item.name : entry.Spec?.Item;
                string state = entry.Error != null ? "refused: " + entry.Error : entry.IsReady ? "ready, " + VanillaEdits.Text(entry.Effects) : "waiting for the prefabs";
                text.Append("  ").Append(entry.Id).Append("  ").Append(target).Append(": ").Append(state).Append(Notes(entry.Warnings))
                    .Append(CookingLibCommands.AliasNotes(entry.Spec?.Effects)).Append('\n');
            }

            text.Append("vanilla liquids: ").Append(VanillaLiquids.All.Count).Append('\n');
            foreach (LibVanillaLiquid entry in VanillaLiquids.All)
            {
                string target = entry.Definition != null ? entry.Definition.name : entry.Spec?.Liquid;
                string state = entry.Error != null ? "refused: " + entry.Error : (entry.IsApplied ? "applied at setup, " : "waiting for PostSetup, ") + VanillaEdits.Text(entry.Effects);
                text.Append("  ").Append(entry.Id).Append("  ").Append(target).Append(": ").Append(state).Append(Notes(entry.Warnings))
                    .Append(CookingLibCommands.AliasNotes(entry.Spec?.Effects)).Append('\n');
            }
        }

        internal static void Dump(StringBuilder text)
        {
            text.Append("== Vanilla recipes now (changed by names the CookingLib edit) ==\n");
            text.Append("page lengths: ").Append(PageBudget.Lengths()).Append('\n');
            foreach (StewRecipe recipe in HashedGeneralValue<StewRecipe>.All.Where(r => r != null && StewRecipes.Find(r) == null).OrderBy(r => r.name, StringComparer.OrdinalIgnoreCase))
            {
                text.Append(recipe.name).Append("  ").Append(recipe.Hash).Append("  ").Append(VanillaRecipes.PageText(recipe.ExtraEffects, recipe.EffectModifiers))
                    .Append("  base ").Append(VanillaRecipes.BaseText(VanillaEdits.PossibleLiquids.GetValue(recipe) as List<LiquidDefinition>));
                VanillaRecipes.Override edit = VanillaRecipes.OverrideOf(recipe);
                if (edit != null && edit.Tint.HasValue)
                    text.Append("  tint ").Append(Rgba(edit.Tint.Value));
                if (edit != null && edit.IgnoresDyes)
                    text.Append("  ignores dyes");
                if (edit != null && edit.Result != null)
                    text.Append("  result ").Append(edit.Result.Lib != null ? edit.Result.Lib.Id : edit.Result.Vanilla.name);
                if (VanillaRecipes.IsDisabled(recipe))
                    text.Append("  disabled");
                text.Append(ChangedBy(recipe)).Append('\n');
            }
            text.Append('\n');

            text.Append("== Vanilla ingredients now (changed by names the CookingLib edit) ==\n");
            foreach (Item item in HashedGeneralValue<Item>.All.Where(i => i != null).OrderBy(i => i.name, StringComparer.OrdinalIgnoreCase))
            {
                CookableItem cookable = SlotItems.Vanilla(item);
                if (cookable == null)
                    continue;
                text.Append(item.name).Append("  ").Append(item.Hash)
                    .Append("  category ").Append(cookable.Category == null ? "none" : cookable.Category.name)
                    .Append("  chunk ").Append(cookable.ChunkVisual == null ? "none" : cookable.ChunkVisual.name)
                    .Append("  effects ").Append(VanillaEdits.Text(cookable.Effects)).Append(ChangedBy(cookable)).Append('\n');
            }
            text.Append('\n');

            text.Append("== Foods and game liquids changed by CookingLib (now, then the game's) ==\n");
            foreach (VanillaEdits.Edit edit in VanillaEdits.All)
            {
                IEnumerable<EffectDefinitionApplication> now = edit.Target is EdibleObject edible ? edible.ConsumptionEffects
                    : edit.Target is LiquidData data ? data.Effects : null;
                if (now != null)
                    text.Append(edit.Label).Append(": now ").Append(VanillaEdits.Text(now)).Append(", game ").Append(edit.Was).Append(", changed by ").Append(edit.By ?? "?").Append('\n');
            }
            text.Append('\n');
        }

        static string RecipeState(LibVanillaRecipe edit)
        {
            if (edit.Error != null)
                return "refused: " + edit.Error;
            if (edit.IsDisable)
                return edit.WorldApplied ? "disabled" : Main.IsServer ? "disable waiting for the world" : "disable, server only";

            List<string> parts = new List<string>();
            if (edit.HasPages)
                parts.Add(VanillaRecipes.PageFields(edit) + (edit.PagesApplied ? " applied at setup" : " not applied"));
            if (edit.HasWorld)
                parts.Add(VanillaRecipes.Fields(edit) + (edit.WorldApplied ? " applied" : " waiting for the world"));
            return string.Join("; ", parts.ToArray());
        }

        static string IngredientState(LibIngredient entry)
        {
            if (entry.Error != null)
                return "refused: " + entry.Error;

            List<string> parts = new List<string>();
            if (entry.HasPages)
                parts.Add(VanillaIngredients.Fields(entry, true) + (entry.PagesApplied ? " applied at setup" : " not applied"));
            if (entry.HasWorld)
                parts.Add(VanillaIngredients.Fields(entry, false) + (entry.WorldApplied ? " applied" : " waiting for the world"));
            string state = string.Join("; ", parts.ToArray());
            if (entry.WorldApplied && entry.Spec.Category != null)
                state += entry.Touched.Count == 0 ? ", touches no recipe" : ", touches " + string.Join(", ", entry.Touched.ToArray());
            return state;
        }

        static string Notes(IReadOnlyList<string> warnings) => warnings == null || warnings.Count == 0 ? "" : "; " + string.Join("; ", warnings.ToArray());

        static string ChangedBy(object target)
        {
            List<string> owners = VanillaEdits.Of(target).GroupBy(e => e.By ?? "?")
                .Select(g => g.Key + " (" + string.Join(", ", g.Select(e => e.Field).ToArray()) + ")").ToList();
            return owners.Count == 0 ? "" : "  changed by " + string.Join(", ", owners.ToArray());
        }

        static string Rgba(UnityEngine.Color c) =>
            string.Join(" ", new[] { c.r, c.g, c.b, c.a }.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)).ToArray());
    }
}
