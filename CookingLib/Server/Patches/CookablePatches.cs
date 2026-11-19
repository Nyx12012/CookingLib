using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alta.Cooking;
using Alta.Inventory;
using Alta.Pages;
using HarmonyLib;

namespace CookingLib
{
    internal static class CookablePatches
    {
        static Type saveType;
        static FieldInfo savedIngredients;

        internal static int Restored { get; private set; }

        internal static int Dropped { get; private set; }

        internal static int PagesSkipped { get; private set; }

        internal static bool LoadOn { get; private set; }

        internal static void Apply(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(CookingPot), nameof(CookingPot.InitializeAsServer)),
                postfix: new HarmonyMethod(typeof(CookablePatches), nameof(InitPostfix)));
            harmony.Patch(AccessTools.Method(typeof(GlobalPagesManager), nameof(GlobalPagesManager.AddPage), new[] { typeof(int), typeof(uint) }),
                prefix: new HarmonyMethod(typeof(CookablePatches), nameof(PagePrefix)));

            saveType = AccessTools.TypeByName("Alta.Cooking.StewManagerSaveData");
            savedIngredients = saveType == null ? null : AccessTools.Field(saveType, "ingredients");
            Type savable = saveType == null ? null : typeof(StewManager).GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.Name.StartsWith("ISavable`", StringComparison.Ordinal) && i.GetGenericArguments()[0] == saveType);
            MethodInfo load = null;
            if (savable != null)
            {
                InterfaceMapping map = typeof(StewManager).GetInterfaceMap(savable);
                load = map.TargetMethods.FirstOrDefault(m => m.Name.EndsWith("Load", StringComparison.Ordinal));
            }
            if (load == null || savedIngredients == null)
                throw new MissingMethodException("StewManager", "ISavable<StewManagerSaveData>.Load");
            harmony.Patch(load, prefix: new HarmonyMethod(typeof(CookablePatches), nameof(LoadPrefix)), postfix: new HarmonyMethod(typeof(CookablePatches), nameof(LoadPostfix)));
            LoadOn = true;
        }

        static void InitPostfix(CookingPot __instance)
        {
            Safe.Run("cookable pot", () => CookablePots.Track(__instance));
        }

        static bool PagePrefix(int providerID, uint hash)
        {
            try
            {
                if (providerID == 6 && Cookables.Any && Cookables.Live.Any(c => c.Item != null && c.Item.Hash == hash) || Edibles.SkipsPage(providerID, hash))
                {
                    PagesSkipped++;
                    return false;
                }
            }
            catch (Exception e)
            {
                Safe.Fail(nameof(CookablePatches) + " pages", e);
            }
            return true;
        }

        static void LoadPrefix(object data)
        {
            try
            {
                if (!(savedIngredients.GetValue(data) is Item[] items) || !items.Any(i => i == null))
                    return;
                Item[] kept = items.Where(i => i != null).ToArray();
                Dropped += items.Length - kept.Length;
                Log.Warn("a pot's save named " + (items.Length - kept.Length) + " ingredients this game no longer knows, they are left out");
                savedIngredients.SetValue(data, kept);
            }
            catch (Exception e)
            {
                Safe.Fail(nameof(CookablePatches) + " load", e);
            }
        }

        static void LoadPostfix(StewManager __instance, object data)
        {
            try
            {
                List<CookableItem> ingredients = __instance.Ingredients;
                if (!ingredients.Any(i => i == null))
                    return;
                Item[] items = savedIngredients.GetValue(data) as Item[] ?? new Item[0];
                for (int i = ingredients.Count - 1; i >= 0; i--)
                {
                    if (ingredients[i] != null)
                        continue;
                    LibIngredient cookable = i < items.Length ? Cookables.ForItem(items[i]) : null;
                    if (cookable != null)
                    {
                        ingredients[i] = cookable.Cookable;
                        Restored++;
                        continue;
                    }
                    Log.Warn("a pot's saved ingredient " + (i < items.Length && items[i] != null ? items[i].name : "?") + " is no stew ingredient now, it is left out");
                    ingredients.RemoveAt(i);
                    Dropped++;
                }
            }
            catch (Exception e)
            {
                Safe.Fail(nameof(CookablePatches) + " load", e);
            }
        }
    }
}
