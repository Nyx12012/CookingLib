using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Alchemy;
using Alta.Inventory;
using Alta.Networking;

namespace CookingLib
{
    /// <summary>Changes what the game's own foods give when eaten, and makes items that are not food edible. Nothing is saved, so without the mod the game's values come back.</summary>
    public static class Edibles
    {
        internal const int MaxBites = 20;
        internal const float MaxEatSeconds = 10f;
        internal const float MinBiteSeconds = 0.05f;

        const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        static readonly FieldInfo prefabHash = typeof(Item).GetField("prefabHash", PrivateInstance);
        static readonly FieldInfo entityField = typeof(NetworkEntityBehaviour).GetField("entity", PrivateInstance);
        static readonly FieldInfo pickupField = typeof(EdibleObject).GetField("pickup", PrivateInstance);
        static readonly FieldInfo stageField = typeof(EdibleObject).GetField("biteEffects", PrivateInstance);
        static readonly FieldInfo biteFxField = typeof(EdibleObject.EatenStage).GetField("biteEffect", PrivateInstance);
        static readonly FieldInfo bitesField = typeof(EdibleObject.EatenStage).GetField("numberOfBites", PrivateInstance);
        static readonly FieldInfo secondsField = typeof(EdibleObject.EatenStage).GetField("biteDuration", PrivateInstance);
        static readonly List<LibEdible> all = new List<LibEdible>();
        static readonly HashSet<uint> injectedPrefabs = new HashSet<uint>();
        static readonly HashSet<uint> injectedItems = new HashSet<uint>();
        static bool late;

        /// <summary>Every change mods asked for and have not reverted, refused ones included.</summary>
        public static IReadOnlyList<LibEdible> All => all;

        /// <summary>Asked on the server before a player starts eating anything, game foods too. Return false to refuse: the player crunches and the item stays. Keep it quick and side effect free, it can be asked several times per bite. A handler that throws allows.</summary>
        public static event Func<EatCheck, bool> CanEat;

        /// <summary>Why the eating guards are off on the server, or null.</summary>
        public static string Error { get; internal set; }

        /// <summary>Changes a game food, or makes an item edible. Call it from Main.Setup on the server and on players' machines. A later change to a game food applies at once; an item to make edible must come from Setup. Never throws.</summary>
        public static LibEdible Register(EdibleSpec spec)
        {
            LibEdible entry = new LibEdible(spec);
            try
            {
                Check(entry);
                string clash = Claims.Take(entry.Id, new[] { Claims.Food(entry.PrefabHash, entry.Item.name, "effects", VanillaEdits.Text(entry.Effects)) });
                if (clash != null)
                    throw new ArgumentException(clash);
            }
            catch (Exception e)
            {
                entry.Error = e is ArgumentException ? e.Message : e.ToString();
            }

            all.Add(entry);
            if (entry.Error != null)
            {
                Log.Warn("food " + entry.Id + " refused: " + entry.Error);
                return entry;
            }

            if (Ready)
                Finish(entry);
            else if (Main.SetupDone && !(Main.IsServer && ServerPatches.PrefabMapOn))
                Walk();
            return entry;
        }

        /// <summary>The change in place on this food, or null when it gives the game's values.</summary>
        public static LibEdible Find(Item item) => item == null ? null : all.FirstOrDefault(e => e.Error == null && e.IsReady && e.Item == item);

        /// <summary>Undoes the change with this id and gives back the game's values, on foods already spawned too. An item made edible stays edible until the server restarts, so that returns false. True if it was in place or waiting.</summary>
        public static bool Revert(string id)
        {
            try
            {
                List<LibEdible> gone = all.Where(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
                LibEdible entry = gone.FirstOrDefault(e => e.Error == null);
                if (entry != null && entry.IsReady && !entry.IsVanillaFood)
                {
                    Log.Warn("food " + entry.Id + " made an item edible, which stays until a restart");
                    return false;
                }
                all.RemoveAll(gone.Contains);
                List<string> freed = new List<string>();
                if (entry != null)
                    freed.AddRange(VanillaEdits.Release(entry.Id));
                else
                    Claims.Forget(id);
                if (entry == null)
                    return false;

                bool applied = entry.IsReady;
                entry.IsReady = false;
                Log.Info("food " + entry.Id + " reverted");
                if (applied && freed.Count > 0 && late)
                    Sweep(new[] { entry.PrefabHash });
                return true;
            }
            catch (Exception e)
            {
                Safe.Fail("food revert", e);
                return false;
            }
        }

        internal static bool Ready { get; private set; }

        internal static int Swept { get; private set; }

        internal static int Injected => injectedPrefabs.Count;

        internal static int RefusedAtStart { get; set; }

        internal static int RefusedByCanEat { get; set; }

        internal static int SkippedAtEnd { get; set; }

        internal static int CanEatHandlers => CanEat == null ? 0 : CanEat.GetInvocationList().Length;

        internal static IEnumerable<LibEdible> Live => all.Where(e => e.Error == null);

        internal static bool IsInjected(uint prefab) => injectedPrefabs.Contains(prefab);

        internal static bool SkipsPage(int provider, uint hash) => provider == 5 && injectedItems.Contains(hash);

        internal static bool Allows(EatCheck check) => EventQueue.Check(CanEat, check, nameof(CanEat));

        internal static void MapReady(bool early)
        {
            Ready = true;
            if (early)
                late = true;
            foreach (LibEdible entry in Live.Where(e => !e.IsReady).ToList())
                Finish(entry);
        }

        internal static void AfterSetup()
        {
            if (!Ready && Live.Any() && !(Main.IsServer && ServerPatches.PrefabMapOn))
                Walk();
            if (!Main.IsServer)
                late = true;
        }

        internal static void WorldUp()
        {
            late = true;
            List<uint> changed = Live.Where(e => e.IsReady && e.IsVanillaFood).Select(e => e.PrefabHash).Distinct().ToList();
            if (changed.Count > 0)
                Sweep(changed);
        }

        static void Walk()
        {
            PrefabWalk.All();
            if (!Ready)
                MapReady(false);
        }

        static void Check(LibEdible entry)
        {
            EdibleSpec spec = entry.Spec;
            if (spec == null)
                throw new ArgumentException("spec is null");
            if (string.IsNullOrEmpty(spec.Id) || spec.Id.IndexOf('.') <= 0)
                throw new ArgumentException("id must look like author.thing");
            if (Live.Any(e => string.Equals(e.Id, spec.Id, StringComparison.OrdinalIgnoreCase)) || Claims.Holds(spec.Id))
                throw new ArgumentException("id is already used, Revert it first");
            if (spec.Bites != 0 && (spec.Bites < 1 || spec.Bites > MaxBites))
                throw new ArgumentException("Bites must be 1 to " + MaxBites + ", or 0 to copy LikeFood");
            if (spec.BiteSeconds != 0f && (float.IsNaN(spec.BiteSeconds) || spec.BiteSeconds < MinBiteSeconds || spec.BiteSeconds > MaxEatSeconds))
                throw new ArgumentException("BiteSeconds must be " + MinBiteSeconds + " to " + MaxEatSeconds + ", or 0 to copy LikeFood");
            if (spec.Bites > 0 && spec.BiteSeconds > 0f && spec.Bites * spec.BiteSeconds > MaxEatSeconds + 0.001f)
                throw new ArgumentException("all bites together must take at most " + MaxEatSeconds + " seconds");
            if (string.IsNullOrEmpty(spec.Item))
                throw new ArgumentException("Item is empty");

            entry.Item = Names.NeedItemExact(spec.Item);
            entry.PrefabHash = prefabHash != null ? (uint)prefabHash.GetValue(entry.Item) : entry.Item.Prefab == null ? 0 : entry.Item.Prefab.Hash;
            if (entry.PrefabHash == 0)
                throw new ArgumentException(entry.Item.name + " has no prefab, so nothing can eat it");
            entry.Effects = EatenEffects(spec.Effects);
        }

        static EffectDefinitionApplication[] EatenEffects(List<EffectSpec> specs)
        {
            if (specs == null)
                throw new ArgumentException("Effects is null, use an empty list for none");
            if (specs.Contains(null))
                throw new ArgumentException("Effects can't hold a null effect");

            List<EffectDefinitionApplication> effects = new List<EffectDefinitionApplication>();
            foreach (EffectSpec spec in specs)
            {
                if (!LiquidFactory.IsWithin100(spec.Strength))
                    throw new ArgumentException("effect strengths must be minus 100 to 100");
                EffectDefinition definition = EffectNames.Need(spec, EffectNames.ForServer);
                if (effects.Any(e => e.EffectToModify == definition))
                    throw new ArgumentException("Effects lists " + definition.name + " twice");
                effects.Add(new EffectDefinitionApplication(definition, spec.Strength, EffectSpread.Check(spec, definition)));
            }
            int shown = effects.Count(e => e.StrengthMultiplier != 0f);
            if (shown > PageBudget.FallbackEffects)
                throw new ArgumentException("a food carries at most " + PageBudget.FallbackEffects + " effects that are not 0, this has " + shown);
            return effects.ToArray();
        }

        static void Finish(LibEdible entry)
        {
            try
            {
                NetworkPrefab prefab = PrefabManager.GetPrefab(entry.PrefabHash);
                if (prefab == null)
                    throw new ArgumentException(entry.Item.name + " has no prefab");
                EdibleObject edible = prefab.GetComponent<EdibleObject>();
                if (edible == null)
                {
                    Inject(entry, prefab);
                    return;
                }
                if (IsForeign(prefab))
                    throw new ArgumentException(entry.Item.name + " was made edible by another mod");
                if (entry.Spec.Bites != 0 || entry.Spec.BiteSeconds != 0f)
                    throw new ArgumentException(entry.Item.name + " is a game food, Bites and BiteSeconds are for items made edible");

                Claims.Claim key = Claims.Food(entry.PrefabHash, entry.Item.name, "effects", null);
                EffectDefinitionApplication[] game = VanillaEdits.Find(key.Key)?.Original as EffectDefinitionApplication[] ?? edible.ConsumptionEffects;
                EffectDefinitionApplication[] effects = VanillaEdits.Replace(entry.Spec.Effects, game, "foods");
                PageBudget.Check(PageKind.Edible, entry.Item.name + "'s food page", effects);
                VanillaEdits.Write(key, edible, VanillaEdits.Text(edible.ConsumptionEffects), VanillaEdits.FoodEffects, effects);
                entry.Effects = effects;
                entry.IsVanillaFood = true;
                entry.IsReady = true;
                Log.Info("food " + entry.Id + ": " + entry.Item.name + " gives " + VanillaEdits.Text(effects));
            }
            catch (Exception e)
            {
                Refuse(entry, e);
                return;
            }

            if (Main.SetupDone)
                entry.Notes.Add("changed after Setup, so books show its old food page until they reload");
            if (late)
                Sweep(new[] { entry.PrefabHash });
        }

        static void Inject(LibEdible entry, NetworkPrefab prefab)
        {
            if (late)
                throw new ArgumentException(entry.Item.name + " is not a food, and items made edible must be registered from Main.Setup");
            if (Main.IsServer && Error != null)
                throw new ArgumentException(Error);
            NetworkEntity entity = prefab.Entity;
            Pickup pickup = entity == null ? null : entity.CommonPickup;
            if (entity == null || pickup == null || entity.gameObject != prefab.gameObject || pickup.gameObject != prefab.gameObject)
                throw new ArgumentException(entry.Item.name + " has no pickup at its root, so a mouth can't take it");

            Item likeItem = Names.NeedItemExact(entry.Spec.LikeFood ?? "");
            NetworkPrefab likePrefab = likeItem.Prefab;
            EdibleObject like = likePrefab == null ? null : likePrefab.GetComponent<EdibleObject>();
            if (like == null || IsForeign(likePrefab))
                throw new ArgumentException("LikeFood " + likeItem.name + " is not a game food");
            if (likePrefab.Entity == null || likePrefab.Entity.CommonPickup == null)
                throw new ArgumentException("LikeFood " + likeItem.name + " has no pickup, pick another food");
            EdibleObject.EatenStage likeStage = (EdibleObject.EatenStage)stageField.GetValue(like);
            if (likeStage == null)
                throw new ArgumentException("LikeFood " + likeItem.name + " has no bites");

            int bites = entry.Spec.Bites > 0 ? entry.Spec.Bites : likeStage.NumberOfBites;
            float seconds = entry.Spec.BiteSeconds > 0f ? entry.Spec.BiteSeconds : likeStage.BiteDuration;
            if (bites * seconds > MaxEatSeconds + 0.001f)
                throw new ArgumentException("all bites together take " + Claims.Number(bites * seconds) + " seconds, at most " + MaxEatSeconds);

            EdibleObject made = prefab.gameObject.AddComponent<EdibleObject>();
            EdibleObject.EatenStage stage = new EdibleObject.EatenStage();
            biteFxField.SetValue(stage, biteFxField.GetValue(likeStage));
            bitesField.SetValue(stage, bites);
            secondsField.SetValue(stage, seconds);
            entityField.SetValue(made, entity);
            pickupField.SetValue(made, pickup);
            stageField.SetValue(made, stage);
            VanillaEdits.FoodEffects.SetValue(made, entry.Effects.Select(e => e.Copy()).ToArray());
            injectedPrefabs.Add(prefab.Hash);
            injectedItems.Add(entry.Item.Hash);
            entry.IsVanillaFood = false;
            entry.IsReady = true;
            Log.Info("food " + entry.Id + ": " + entry.Item.name + " made edible, " + bites + " bites of " + Claims.Number(seconds) + " s like " + likeItem.name + ", gives " + VanillaEdits.Text(entry.Effects));
        }

        static bool IsForeign(NetworkPrefab prefab)
        {
            if (injectedPrefabs.Contains(prefab.Hash) || prefab.Entity == null)
                return false;
            try
            {
                return prefab.Entity.GetComponentOnEntity<EdibleObject>() == null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void Refuse(LibEdible entry, Exception e)
        {
            entry.Error = e is ArgumentException ? e.Message : e.ToString();
            entry.IsReady = false;
            VanillaEdits.Release(entry.Id);
            Log.Warn("food " + entry.Id + " refused: " + entry.Error);
        }

        static void Sweep(IEnumerable<uint> hashes)
        {
            HashSet<uint> wanted = new HashSet<uint>(hashes);
            int count = Safe.Run("food sweep", () => SweepNow(wanted), 0);
            Swept += count;
            Log.Info("foods: swept " + count);
        }

        static int SweepNow(HashSet<uint> hashes)
        {
            int count = 0;
            foreach (EdibleObject live in UnityEngine.Object.FindObjectsOfType<EdibleObject>())
            {
                NetworkPrefab own = live.GetComponent<NetworkPrefab>();
                if (own == null || !hashes.Contains(own.Hash))
                    continue;

                NetworkPrefab prefab = PrefabManager.GetPrefab(own.Hash);
                EdibleObject source = prefab == null ? null : prefab.GetComponent<EdibleObject>();
                if (source == null || source == live || VanillaEdits.Same(live.ConsumptionEffects, source.ConsumptionEffects))
                    continue;

                VanillaEdits.FoodEffects.SetValue(live, source.ConsumptionEffects.ToArray());
                count++;
            }
            return count;
        }
    }
}
