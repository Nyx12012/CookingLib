using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Cooking;
using Alta.Global;
using Alta.Inventory;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CookingLib
{
    internal static class CookablePots
    {
        static readonly AccessTools.FieldRef<CookingPot, CookingPot.CookingStage> stage = AccessTools.FieldRefAccess<CookingPot, CookingPot.CookingStage>("currentStage");
        static readonly AccessTools.FieldRef<CookingPot, CookingPotVolume> volume = AccessTools.FieldRefAccess<CookingPot, CookingPotVolume>("potVolume");
        static readonly AccessTools.FieldRef<CookingPot, StewManager> manager = AccessTools.FieldRefAccess<CookingPot, StewManager>("stewManager");
        static readonly AccessTools.FieldRef<StewManager, StewComponent> stewOf = AccessTools.FieldRefAccess<StewManager, StewComponent>("stew");
        static readonly AccessTools.FieldRef<StewManager, List<Player>> cooks = AccessTools.FieldRefAccess<StewManager, List<Player>>("cookingPlayers");
        static readonly Action<CookingPot> startStewing = AccessTools.MethodDelegate<Action<CookingPot>>(AccessTools.Method(typeof(CookingPot), "StartStewing"));
        static readonly Func<StewManager, bool> checkForRecipe = AccessTools.MethodDelegate<Func<StewManager, bool>>(AccessTools.Method(typeof(StewManager), "CheckForRecipe"));
        static readonly Action<StewManager, bool> updateLiquid = AccessTools.MethodDelegate<Action<StewManager, bool>>(AccessTools.Method(typeof(StewManager), "UpdateLiquidContainer"));
        static readonly Action<StewManager, NetworkEntity> fadeOut = AccessTools.MethodDelegate<Action<StewManager, NetworkEntity>>(AccessTools.Method(typeof(StewManager), "FadeOutEntity"));

        static readonly Dictionary<int, Tracked> pots = new Dictionary<int, Tracked>();
        static readonly List<int> gone = new List<int>();

        internal static int Converted { get; private set; }

        internal static int PushedOut { get; private set; }

        internal static int Failed { get; private set; }

        internal static int PotsWithCookables => pots.Values.Count(p => p.Pot != null && p.Holds > 0);

        internal static void Track(CookingPot pot)
        {
            if (pot == null || pots.ContainsKey(pot.GetInstanceID()))
                return;
            CookingPotVolume v = volume(pot);
            if (v == null || pot.CookingLiquid == null)
                return;

            Tracked entry = new Tracked(pot);
            pots[pot.GetInstanceID()] = entry;
            v.IngredientAdded += go => Safe.Run("cookable added", () => Added(entry, go, true));
            pot.CookingLiquid.LiquidEmptied += () => entry.Clear();
            foreach (GameObject go in v.ContainedCookables.ToList())
                Safe.Run("cookable added", () => Added(entry, go, false));
        }

        internal static void Tick(float seconds)
        {
            if (pots.Count == 0)
                return;

            gone.Clear();
            bool any = Cookables.Any;
            foreach (KeyValuePair<int, Tracked> pair in pots)
            {
                Tracked entry = pair.Value;
                if (entry.Pot == null)
                {
                    gone.Add(pair.Key);
                    continue;
                }
                if (any)
                    Safe.Run("cookable tick", () => TickPot(entry, seconds));
            }
            foreach (int key in gone)
                pots.Remove(key);
        }

        static void Added(Tracked entry, GameObject go, bool live)
        {
            if (!Cookables.Any || go == null)
                return;
            Pickup pickup = go.GetComponent<Pickup>();
            LibIngredient cookable = pickup == null ? null : Cookables.ForItem(pickup.Item);
            if (cookable == null)
                return;
            if (live && stage(entry.Pot) == CookingPot.CookingStage.StewLocked && ((CookableSpec)cookable.Spec).PushOutWhenLocked)
            {
                NetworkRigidbody body = pickup.Entity.PrefabRoot.GetComponent<NetworkRigidbody>();
                if (body != null)
                {
                    body.Topic.AddForce(GlobalSettings<CookingSettings>.Instance.GenerateIngredientRejectionForce());
                    cookable.PushedOut++;
                    PushedOut++;
                }
            }
        }

        static void TickPot(Tracked entry, float seconds)
        {
            CookingPotVolume v = volume(entry.Pot);
            entry.Holds = 0;
            if (v == null || v.ContainedCookables.Count == 0)
            {
                entry.Converted.Clear();
                return;
            }
            if (entry.Converted.Count > 0)
            {
                HashSet<int> inside = new HashSet<int>(v.ContainedCookables.Where(go => go != null).Select(go => go.GetComponent<Pickup>()).Where(p => p != null).Select(p => p.GetInstanceID()));
                entry.Converted.RemoveWhere(id => !inside.Contains(id));
            }

            CookingPot.CookingStage now = stage(entry.Pot);
            bool open = now == CookingPot.CookingStage.WarmingUp || now == CookingPot.CookingStage.Stewing;
            bool hot = entry.Pot.CookingLiquid.Temperature > GlobalSettings<CookingSettings>.Instance.MinimumStewingTemperature;
            foreach (GameObject go in v.ContainedCookables.ToList())
            {
                if (go == null)
                    continue;
                Pickup pickup = go.GetComponent<Pickup>();
                LibIngredient cookable = pickup == null ? null : Cookables.ForItem(pickup.Item);
                if (cookable == null)
                    continue;
                entry.Holds++;
                int id = pickup.GetInstanceID();
                if (pickup.IsInteractedWith)
                    entry.Soak.Remove(id);
                if (!open || !hot || entry.Converted.Contains(id) || pickup.IsInteractedWith || pickup.IsDocked || !MaterialFits(cookable, pickup))
                    continue;

                entry.Soak.TryGetValue(id, out float soaked);
                soaked += seconds;
                entry.Soak[id] = soaked;
                if (soaked < ((CookableSpec)cookable.Spec).SoakSeconds)
                    continue;

                entry.Converted.Add(id);
                if (!Convert(entry.Pot, pickup, cookable))
                    return;
                now = stage(entry.Pot);
                if (now != CookingPot.CookingStage.WarmingUp && now != CookingPot.CookingStage.Stewing)
                    return;
            }
        }

        static bool MaterialFits(LibIngredient cookable, Pickup pickup)
        {
            if (cookable.Materials == null)
                return true;
            PhysicalMaterialPart part = pickup.GetComponentInChildren<PhysicalMaterialPart>();
            return part != null && cookable.Materials.Contains(part.PhysicalMaterial);
        }

        static bool Convert(CookingPot pot, Pickup pickup, LibIngredient cookable)
        {
            try
            {
                StewManager stews = manager(pot);
                startStewing(pot);
                StewComponent stew = stewOf(stews);
                if (stew == null)
                    throw new InvalidOperationException("the pot made no stew");
                stew.ResetCookableProgress();
                stews.Ingredients.Add(cookable.Cookable);
                PlayerController holder = pickup.LastPlayerInteractor;
                cooks(stews).Add(holder == null ? null : holder.NetworkPlayer);
                if (!checkForRecipe(stews))
                    updateLiquid(stews, false);
                fadeOut(stews, pickup.Entity);
                cookable.Converted++;
                Converted++;
                return true;
            }
            catch (Exception e)
            {
                Failed++;
                Safe.Fail("cookable stewing", e);
                return false;
            }
        }

        sealed class Tracked
        {
            internal Tracked(CookingPot pot)
            {
                Pot = pot;
            }

            internal CookingPot Pot { get; }

            internal HashSet<int> Converted { get; } = new HashSet<int>();

            internal Dictionary<int, float> Soak { get; } = new Dictionary<int, float>();

            internal int Holds { get; set; }

            internal void Clear()
            {
                Soak.Clear();
            }
        }
    }
}
