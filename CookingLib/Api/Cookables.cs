using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Cooking;
using Alta.Inventory;
using Alta.Networking;
using UnityEngine;

namespace CookingLib
{
    /// <summary>Makes items that are not food into stew ingredients, such as an ore or a wedge. Server only: vanilla players can join and see them dissolve.</summary>
    public static class Cookables
    {
        internal const float MaxSoak = 60f;

        static readonly FieldInfo materialsField = typeof(ItemComponent).GetField("physicalMaterials", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly List<LibIngredient> all = new List<LibIngredient>();
        static readonly Dictionary<int, LibIngredient> byItem = new Dictionary<int, LibIngredient>();

        /// <summary>Every cookable mods asked for, refused ones included.</summary>
        public static IReadOnlyList<LibIngredient> All => all;

        /// <summary>Why cookables are off, or null.</summary>
        public static string Error { get; internal set; }

        /// <summary>Makes an item a stew ingredient. Call it from Main.Setup. Server only, a player's CookingLib keeps it inert. Never throws.</summary>
        public static LibIngredient Register(CookableSpec spec)
        {
            LibIngredient entry = new LibIngredient(spec) { IsCookable = true };
            try
            {
                Check(entry);
                if (Main.IsServer)
                {
                    string clash = Claims.Take(entry.Id, new[] { Claims.Ingredient(entry.Item, "cookable", spec.Id) });
                    if (clash != null)
                        throw new ArgumentException(clash);
                    entry.Cookable = Build(entry);
                    byItem[entry.Item.GetInstanceID()] = entry;
                    entry.IsApplied = true;
                }
            }
            catch (Exception e)
            {
                entry.Error = e is ArgumentException ? e.Message : e.ToString();
            }

            all.Add(entry);
            if (entry.Error != null)
                Log.Warn("cookable " + (spec?.Id ?? "null") + " refused: " + entry.Error);
            else if (Main.IsServer)
                Log.Info("cookable " + spec.Id + ": " + entry.Item.name + " stews after " + spec.SoakSeconds + " s, " + VanillaEdits.Text(entry.Effects));
            return entry;
        }

        /// <summary>The ready cookable for an item name or number, or null.</summary>
        public static LibIngredient Find(string item)
        {
            Item found = Names.Find<Item>(item);
            return found == null ? null : ForItem(found);
        }

        internal static bool Any => byItem.Count > 0;

        internal static IEnumerable<LibIngredient> Live => all.Where(e => e.Error == null && e.IsApplied);

        internal static LibIngredient ForItem(Item item) =>
            item != null && byItem.TryGetValue(item.GetInstanceID(), out LibIngredient entry) && entry.Error == null ? entry : null;

        internal static bool IsCookableItem(CookableItem cookable) => cookable != null && byItem.Values.Any(e => e.Cookable == cookable);

        internal static void WorldUp()
        {
            foreach (LibIngredient entry in Live.ToList())
            {
                string problem = Safe.Run("cookable prefab check", () => PrefabProblem(entry.Item), null);
                if (problem == null)
                {
                    if (Infusions.All.Any(i => i.Error == null && string.Equals(Names.Norm(i.Spec?.Item), Names.Norm(entry.Item.name))))
                        entry.Notes.Add("also infuses liquids: in a pot with no ingredients it changes the base, after that it stews");
                    if (entry.Item.Prefab != null && entry.Item.Prefab.GetComponentInChildren<Cookable>(true) != null)
                        entry.Notes.Add("its prefab also roasts on a fire");
                    continue;
                }
                entry.Error = problem;
                entry.IsApplied = false;
                byItem.Remove(entry.Item.GetInstanceID());
                Claims.Forget(entry.Id);
                Log.Warn("cookable " + entry.Id + " refused: " + problem);
            }
        }

        internal static string PrefabProblem(Item item)
        {
            NetworkPrefab prefab = item.Prefab;
            if (prefab == null)
                return item.name + " has no prefab";
            Pickup pickup = prefab.GetComponent<Pickup>();
            if (pickup == null)
                return item.name + " has no pickup at its root, so a pot can't take it";
            if (prefab.GetComponent<NetworkRigidbody>() == null)
                return item.name + " has no networked body at its root, so a locked pot can't push it out";
            if (prefab.GetComponentInChildren<StewComponent>(true) != null)
                return item.name + " is a stew";
            if (pickup.SubDocks != null && pickup.SubDocks.Docks.Any())
                return item.name + " holds other items, which would vanish with it";
            if (pickup.CraftingPart != null)
                return item.name + " is a crafting part, which would vanish with what it is attached to";
            return null;
        }

        static void Check(LibIngredient entry)
        {
            CookableSpec spec = (CookableSpec)entry.Spec;
            if (spec == null)
                throw new ArgumentException("spec is null");
            if (string.IsNullOrEmpty(spec.Id) || spec.Id.IndexOf('.') <= 0)
                throw new ArgumentException("id must look like author.thing");
            if (all.Any(e => e.Error == null && string.Equals(e.Id, spec.Id, StringComparison.OrdinalIgnoreCase)) || Claims.Holds(spec.Id))
                throw new ArgumentException("id is already used");
            if (Main.IsServer && Error != null)
                throw new ArgumentException(Error);
            if (Main.WorldIsUp)
                throw new ArgumentException("register cookables from Main.Setup, recipes resolve their slots when the world comes up");
            if (string.IsNullOrEmpty(spec.Item))
                throw new ArgumentException("Item is empty");
            entry.Item = Names.NeedItemExact(spec.Item);
            CookableItem own = entry.Item.GetComponent<CookableItem>();
            if (own is CookingGarnish)
                throw new ArgumentException(entry.Item.name + " is a garnish");
            if (own != null)
                throw new ArgumentException(entry.Item.name + " is already a stew ingredient, change it with VanillaIngredients.Modify");
            if (float.IsNaN(spec.SoakSeconds) || spec.SoakSeconds < 0f || spec.SoakSeconds > MaxSoak)
                throw new ArgumentException("SoakSeconds must be 0 to " + MaxSoak);

            entry.Effects = VanillaEdits.Effects(spec.Effects, "Effects") ?? new Alta.Alchemy.EffectDefinitionApplication[0];
            int shown = entry.Effects.Count(e => e.StrengthMultiplier != 0f);
            if (shown > PageBudget.FallbackEffects)
                throw new ArgumentException("a stew ingredient carries at most " + PageBudget.FallbackEffects + " effects that are not 0, this has " + shown);
            if (!string.IsNullOrEmpty(spec.Chunk))
                entry.Chunk = Names.Need<LiquidVisualChunkDefinition>(spec.Chunk, "chunk", "Chunks");
            if (!string.IsNullOrEmpty(spec.Category))
                entry.Category = Names.NeedCategory(spec.Category);
            if (spec.Materials != null)
            {
                if (spec.Materials.Count == 0 || spec.Materials.Any(string.IsNullOrEmpty))
                    throw new ArgumentException("Materials needs names, or null for any");
                entry.Materials = spec.Materials.Select(m => Names.Need<PhysicalMaterial>(m, "physical material", "Physical materials")).ToList();
            }
            if (entry.Item.Prefab == null)
                throw new ArgumentException(entry.Item.name + " has no prefab");
        }

        static CookableItem Build(LibIngredient entry)
        {
            CookableItem cookable = ScriptableObject.CreateInstance<CookableItem>();
            cookable.name = "cookinglib " + entry.Id;
            cookable.hideFlags = HideFlags.DontUnloadUnusedAsset;
            VanillaEdits.CookableEffects.SetValue(cookable, entry.Effects.ToArray());
            VanillaEdits.CookableChunk.SetValue(cookable, entry.Chunk);
            VanillaEdits.CookableCategory.SetValue(cookable, entry.Category);
            materialsField.SetValue(cookable, new List<PhysicalMaterial>());
            ((IItemComponent)cookable).Initialize(entry.Item);
            return cookable;
        }
    }
}
