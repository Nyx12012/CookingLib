using System;
using System.Collections.Generic;
using System.Linq;
using Alta;
using Alta.Alchemy;
using Alta.Inventory;
using Alta.Liquid;
using Alta.Networking;
using Alta.Pages;
using HarmonyLib;

namespace CookingLib
{
    internal static class EdiblePatches
    {
        static readonly Dictionary<long, float> lastRefusal = new Dictionary<long, float>();

        internal static void Apply(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(ConsumptionManager), nameof(ConsumptionManager.CanConsumeEdible)),
                prefix: new HarmonyMethod(typeof(EdiblePatches), nameof(CanPrefix)));
            harmony.Patch(AccessTools.Method(typeof(EdiblePageProvider), nameof(EdiblePageProvider.GetRandom)),
                prefix: new HarmonyMethod(typeof(EdiblePatches), nameof(PagePrefix)));
            harmony.Patch(AccessTools.Method(typeof(ConsumptionManager), "HandleConsumptionEffects"),
                prefix: new HarmonyMethod(typeof(EdiblePatches), nameof(EffectsPrefix)));
        }

        static bool CanPrefix(ConsumptionManager __instance, EdibleObject edible, ref bool __result)
        {
            try
            {
                if (edible == null)
                    return true;
                NetworkPrefab own = edible.GetComponent<NetworkPrefab>();
                bool ours = own != null && Edibles.IsInjected(own.Hash);
                Pickup pickup = edible.GetComponent<Pickup>();
                if (ours && (pickup == null || pickup.IsDocked || HoldsSomething(pickup) || HoldsLiquid(edible)))
                {
                    Count(__instance, edible, true);
                    __result = false;
                    return false;
                }
                if (Edibles.CanEatHandlers == 0)
                    return true;

                Player player = PlayerIds.Of(__instance);
                Item item = pickup == null ? null : pickup.Item;
                if (!Edibles.Allows(new EatCheck(player, PlayerIds.InfoOf(player), item, Edibles.Find(item))))
                {
                    Count(__instance, edible, false);
                    __result = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                Safe.Fail(nameof(EdiblePatches) + " start", e);
            }
            return true;
        }

        static bool HoldsSomething(Pickup pickup) =>
            pickup.SubDocks != null && pickup.SubDocks.TotalDocks.Any(dock => dock != null && dock.HasDockedPickup);

        static bool HoldsLiquid(EdibleObject edible)
        {
            LiquidContainer container = edible.GetComponentInChildren<LiquidContainer>();
            return container != null && !container.IsEmpty;
        }

        static void Count(ConsumptionManager consumer, EdibleObject edible, bool atStart)
        {
            long key = ((long)consumer.GetInstanceID() << 32) ^ (uint)edible.GetInstanceID();
            float now = UnityEngine.Time.time;
            if (lastRefusal.TryGetValue(key, out float last) && now - last < 2f)
            {
                lastRefusal[key] = now;
                return;
            }
            lastRefusal[key] = now;
            if (lastRefusal.Count > 256)
                lastRefusal.Clear();
            if (atStart)
                Edibles.RefusedAtStart++;
            else
                Edibles.RefusedByCanEat++;
        }

        static bool PagePrefix(EdiblePageProvider __instance, ref PageData __result)
        {
            try
            {
                if (Edibles.Injected == 0)
                    return true;
                List<Item> foods = HashedGeneralValue<Item>.All.Where(i => i != null && i.Prefab != null && i.Prefab.GetComponent<EdibleObject>() != null && !Edibles.IsInjected(i.Prefab.Hash)).ToList();
                if (foods.Count == 0)
                {
                    __result = null;
                    return false;
                }
                Item item = foods[UnityEngine.Random.Range(0, foods.Count)];
                __result = GlobalPagesManager.IsNew(__instance.Identifier, item.Hash) ? __instance.GeneratePage(item) : null;
                return false;
            }
            catch (Exception e)
            {
                Safe.Fail(nameof(EdiblePatches) + " pages", e);
                return true;
            }
        }

        static bool EffectsPrefix(ConsumptionManager __instance, IEnumerable<EffectDefinitionApplication> effects)
        {
            try
            {
                if (Edibles.Injected == 0 || effects == null)
                    return true;
                EdibleObject edible = EatPatch.Eating(__instance, effects);
                if (edible == null)
                    return true;
                NetworkPrefab own = edible.GetComponent<NetworkPrefab>();
                Pickup pickup = edible.GetComponent<Pickup>();
                if (own != null && Edibles.IsInjected(own.Hash) && pickup != null && pickup.IsDocked)
                {
                    Edibles.SkippedAtEnd++;
                    return false;
                }
            }
            catch (Exception e)
            {
                Safe.Fail(nameof(EdiblePatches) + " end", e);
            }
            return true;
        }
    }
}
