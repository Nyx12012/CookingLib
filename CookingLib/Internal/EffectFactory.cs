using System;
using System.Linq;
using System.Reflection;
using Alta;
using Alta.Alchemy;
using Alta.Pages;
using Alta.StatSystem;
using UnityEngine;

namespace CookingLib
{
    internal static class EffectFactory
    {
        internal const int MaxSeconds = 86400;
        internal const uint FullnessStat = 34710;

        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        const int DurationFlag = 1;

        static readonly FieldInfo buffStat = typeof(IndirectStatModifierDefinition).GetField("targetStat", Private);
        static readonly FieldInfo buffCurve = typeof(IndirectStatModifierDefinition).GetField("curveOverDuration", Private);
        static readonly FieldInfo buffDuration = typeof(IndirectStatModifierDefinition).GetField("duration", Private);
        static readonly FieldInfo buffBase = typeof(IndirectStatModifierDefinition).GetField("baseValue", Private);
        static readonly FieldInfo buffUsage = typeof(IndirectStatModifierDefinition).GetField("strengthAffects", Private);
        static readonly FieldInfo changeStat = typeof(StatEffect).GetField("statToAffect", Private);
        static readonly FieldInfo landing = typeof(TeleportEffect).GetField("landingTarget", Private);
        static readonly FieldInfo chance = typeof(ChanceBasedEffectDefinition).GetField("chanceMultiplier", Private);

        internal static EffectKind KindOf(EffectDefinition template)
        {
            if (template is IndirectStatModifierDefinition)
                return EffectKind.Buff;
            if (template is StatChangeEffect)
                return EffectKind.Change;
            if (template is TeleportEffect)
                return EffectKind.Teleport;
            if (template != null && template.GetType() == typeof(InateEffect))
                return EffectKind.Tag;
            throw new ArgumentException("effect " + (template == null ? "none" : template.name) + " can't be copied: use a buff such as Speed, a stat change such as Heal, a teleport, or Inate");
        }

        internal static EffectDefinition Build(LibEffect entry)
        {
            CustomEffectSpec spec = entry.Spec;
            bool hasTemplate = !string.IsNullOrEmpty(spec.Template);
            if (hasTemplate == (spec.Handler != null))
                throw new ArgumentException(hasTemplate ? "give a Template or a Handler, not both" : "give a Template to copy or a Handler");

            EffectDefinition effect;
            if (spec.Handler != null)
            {
                entry.Kind = EffectKind.Code;
                RefuseFields(spec, EffectKind.Code);
                CookingLibEffect code = ScriptableObject.CreateInstance<CookingLibEffect>();
                if (code == null)
                    throw new ArgumentException("this game can't make code effects (the effect object was not created)");
                code.Owner = entry;
                effect = code;
            }
            else
            {
                EffectDefinition template = EffectNames.Need(spec.Template, EffectKinds.Game);
                entry.Kind = KindOf(template);
                entry.Template = template.name;
                RefuseFields(spec, entry.Kind);
                effect = UnityEngine.Object.Instantiate(template);
                Override(effect, spec, entry);
            }

            if (!string.IsNullOrEmpty(spec.Glyph))
                effect.effectGlyph = Names.Need<Glyph>(spec.Glyph, "glyph", "Glyphs");
            effect.name = spec.Id;
            effect.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Registry.SetHash(effect, entry.Number);
            return effect;
        }

        internal static EffectDefinition SecondsCopy(EffectDefinition template, int seconds, uint number, string id)
        {
            IndirectStatModifierDefinition buff = template as IndirectStatModifierDefinition;
            if (buff == null)
                throw new ArgumentException("Seconds only works on buffs such as Speed, not " + template.name);
            CheckSeconds(seconds);
            CheckBuff(buff);
            IndirectStatModifierDefinition copy = UnityEngine.Object.Instantiate(buff);
            buffDuration.SetValue(copy, (uint)seconds);
            copy.name = id;
            copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Registry.SetHash(copy, number);
            return copy;
        }

        internal static void CheckSeconds(int seconds)
        {
            if (seconds < 1 || seconds > MaxSeconds)
                throw new ArgumentException("Seconds must be 1 to " + MaxSeconds);
        }

        internal static uint Seconds(EffectDefinition effect) =>
            effect is IndirectStatModifierDefinition && buffDuration != null ? (uint)buffDuration.GetValue(effect) : 0u;

        internal static StatDefinition StatOf(EffectDefinition effect)
        {
            if (effect is IndirectStatModifierDefinition buff)
                return buff.TargetStat;
            if (effect is StatEffect change)
                return change.StatToAffect;
            return null;
        }

        internal static float BaseOf(EffectDefinition effect) => effect is IndirectStatModifierDefinition ? (float)buffBase.GetValue(effect) : 0f;

        internal static string CurveOf(EffectDefinition effect)
        {
            Curve curve = effect is IndirectStatModifierDefinition ? buffCurve.GetValue(effect) as Curve : null;
            return curve == null ? "none" : curve.name + " " + curve.Hash;
        }

        internal static bool ScalesDuration(EffectDefinition effect) =>
            effect is IndirectStatModifierDefinition && (Convert.ToInt32(buffUsage.GetValue(effect)) & DurationFlag) != 0;

        internal static string Destination(EffectDefinition effect) => effect is TeleportEffect ? landing.GetValue(effect).ToString() : null;

        internal static float ChanceOf(EffectDefinition effect) => effect is ChanceBasedEffectDefinition ? (float)chance.GetValue(effect) : 0f;

        static void Override(EffectDefinition effect, CustomEffectSpec spec, LibEffect entry)
        {
            switch (entry.Kind)
            {
                case EffectKind.Buff:
                    if (spec.Stat != null)
                        buffStat.SetValue(effect, Names.Need<StatDefinition>(spec.Stat, "stat", "Stats"));
                    if (spec.BaseValue.HasValue)
                    {
                        if (!LiquidFactory.IsWithin100(spec.BaseValue.Value))
                            throw new ArgumentException("BaseValue must be minus 100 to 100");
                        buffBase.SetValue(effect, spec.BaseValue.Value);
                    }
                    if (spec.Seconds.HasValue)
                    {
                        CheckSeconds(spec.Seconds.Value);
                        buffDuration.SetValue(effect, (uint)spec.Seconds.Value);
                    }
                    CheckBuff((IndirectStatModifierDefinition)effect);
                    break;
                case EffectKind.Change:
                    if (spec.Stat != null)
                        changeStat.SetValue(effect, Names.Need<StatDefinition>(spec.Stat, "stat", "Stats"));
                    StatDefinition stat = ((StatEffect)effect).StatToAffect;
                    if (stat == null)
                        throw new ArgumentException("the copy has no stat, set Stat");
                    if (!entry.IsShared && stat.Hash == FullnessStat)
                        throw new ArgumentException("a server only effect can't change " + stat.name + ": players judge how full they are with their own copy, so they and the server would disagree. Use the game's Feed");
                    break;
                case EffectKind.Teleport:
                    if (spec.Destination != null)
                    {
                        string key = Names.Norm(spec.Destination);
                        object area = Enum.GetValues(typeof(SpawnAreaIdentifier)).Cast<object>().FirstOrDefault(v => Names.Norm(v.ToString()) == key);
                        if (area == null)
                            throw new ArgumentException("no spawn area named '" + spec.Destination + "'" + Names.Suggest(spec.Destination, Enum.GetNames(typeof(SpawnAreaIdentifier)), "Spawn areas"));
                        landing.SetValue(effect, area);
                    }
                    if (spec.Chance.HasValue)
                    {
                        if (float.IsNaN(spec.Chance.Value) || spec.Chance.Value <= 0f || spec.Chance.Value > 100f)
                            throw new ArgumentException("Chance must be above 0, at most 100");
                        chance.SetValue(effect, spec.Chance.Value);
                    }
                    break;
            }
        }

        static void CheckBuff(IndirectStatModifierDefinition buff)
        {
            if (ScalesDuration(buff))
                throw new ArgumentException("effect " + buff.name + " uses strength for its length, which a negative strength turns into a buff lasting years. Copy one of the game's four buffs");
            if (buffCurve.GetValue(buff) as Curve == null)
                throw new ArgumentException("effect " + buff.name + " has no curve");
            StatDefinition stat = buff.TargetStat;
            if (stat == null)
                throw new ArgumentException("the copy has no stat, set Stat");
            float rest = stat.IndirectModificationCurve == null ? 1f : stat.IndirectModificationCurve.Evaluate(0f);
            if (stat.IsIndirectUsageMultiplier && Mathf.Abs(rest - 1f) > 0.01f)
                throw new ArgumentException("stat " + stat.name + " reads " + rest.ToString("0.##") + " times with no buff on it, so a buff there would not start from the stat's own value. Pick a stat the game's buffs use");
        }

        static void RefuseFields(CustomEffectSpec spec, EffectKind kind)
        {
            bool buff = kind == EffectKind.Buff, change = kind == EffectKind.Change, teleport = kind == EffectKind.Teleport;
            if (spec.Stat != null && !buff && !change)
                throw new ArgumentException("Stat is for buffs and stat changes, not " + kind);
            if ((spec.BaseValue.HasValue || spec.Seconds.HasValue) && !buff)
                throw new ArgumentException("BaseValue and Seconds are for buffs, not " + kind);
            if ((spec.Destination != null || spec.Chance.HasValue) && !teleport)
                throw new ArgumentException("Destination and Chance are for teleports, not " + kind);
        }
    }
}
