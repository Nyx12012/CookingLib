using System;
using System.Linq;
using System.Reflection;
using Alta.Alchemy;
using Alta.Liquid;
using Alta.Serialization;
using HarmonyLib;

namespace CookingLib
{
    internal static class EffectWirePatch
    {
        static readonly FieldInfo effectsField = typeof(LiquidData).GetField("effects", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static bool Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                if (effectsField == null)
                    throw new MissingFieldException("LiquidData", "effects");
                harmony.Patch(AccessTools.Method(typeof(LiquidData), "SerializeEffectsArray"),
                    prefix: new HarmonyMethod(typeof(EffectWirePatch), nameof(Prefix)),
                    finalizer: new HarmonyMethod(typeof(EffectWirePatch), nameof(Finalizer)));
                harmony.Patch(AccessTools.Method(typeof(LiquidData), nameof(LiquidData.Load)),
                    postfix: new HarmonyMethod(typeof(EffectWirePatch), nameof(LoadPostfix)));
                return true;
            }
            catch (Exception e)
            {
                Effects.Error = "the effect patches failed, server only effects still reach players as 0, which reads as nothing: " + e.Message;
                Log.Error("effect patches failed to apply: " + e);
                return false;
            }
        }

        static void Prefix(LiquidData __instance, Stream stream, out EffectDefinitionApplication[] __state)
        {
            __state = null;
            try
            {
                if (!stream.IsWriting)
                    return;
                EffectDefinitionApplication[] effects = (EffectDefinitionApplication[])effectsField.GetValue(__instance);
                if (effects == null || !effects.Any(e => e != null && Effects.KeepOffWire(e.EffectToModify)))
                    return;

                __state = effects;
                effectsField.SetValue(__instance, effects.Where(e => e == null || !Effects.KeepOffWire(e.EffectToModify)).ToArray());
                Effects.Filtered++;
            }
            catch (Exception e)
            {
                Safe.Fail(nameof(EffectWirePatch), e);
            }
        }

        static Exception Finalizer(LiquidData __instance, EffectDefinitionApplication[] __state, Exception __exception)
        {
            if (__state != null)
                effectsField.SetValue(__instance, __state);
            return __exception;
        }

        static void LoadPostfix(LiquidData __instance)
        {
            try
            {
                EffectDefinitionApplication[] effects = (EffectDefinitionApplication[])effectsField.GetValue(__instance);
                if (effects == null || !effects.Any(e => e == null || e.EffectToModify == null))
                    return;
                EffectDefinitionApplication[] kept = effects.Where(e => e != null && e.EffectToModify != null).ToArray();
                Effects.DroppedOnLoad += effects.Length - kept.Length;
                effectsField.SetValue(__instance, kept);
            }
            catch (Exception e)
            {
                Safe.Fail(nameof(EffectWirePatch) + " load", e);
            }
        }
    }
}
