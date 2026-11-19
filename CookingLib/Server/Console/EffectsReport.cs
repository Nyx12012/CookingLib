using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Alta;
using Alta.Alchemy;
using Alta.StatSystem;

namespace CookingLib
{
    internal static class EffectsReport
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        static readonly FieldInfo indirectField = typeof(StatManager).GetField("indirectModifiers", Private);
        static readonly FieldInfo trackedField = typeof(IndirectStatModifier).GetField("trackedModifiers", Private);
        static readonly FieldInfo sumField = typeof(IndirectStatModifier).GetField("value", Private);
        static readonly FieldInfo durationField = typeof(IndirectModifier).GetField("duration", Private);
        static readonly FieldInfo tickField = typeof(IndirectModifier).GetField("tick", Private);

        internal static void Status(StringBuilder text)
        {
            List<LibEffect> mine = Effects.All.ToList();
            List<LibEffect> placeholders = Effects.Hidden.Where(e => EffectNames.KindOf(e.Definition) == EffectKinds.Placeholder).ToList();
            List<LibEffect> lengths = Effects.Hidden.Where(e => e.Error == null && EffectNames.KindOf(e.Definition) == EffectKinds.ServerOnly).ToList();
            int book = Safe.Run("effect book", () => EffectBook.All.Count, 0);
            text.Append("effects: ").Append(mine.Count(e => e.Error == null)).Append(" registered, ").Append(mine.Count(e => e.Error != null)).Append(" refused, ")
                .Append(lengths.Count).Append(" buff lengths; ");
            if (Effects.Error != null)
                text.Append(Effects.Error);
            else
                text.Append("wire filter ").Append(Effects.WirePatchOn ? "on, " + Effects.Filtered + " sends filtered" : "off, nothing to filter");
            text.Append("; book ").Append(book).Append(", ").Append(placeholders.Count).Append(" placeholders; ").Append(Effects.DroppedOnLoad).Append(" dropped on load; ")
                .Append(mine.Sum(e => e.HandlerErrors)).Append(" handler errors\n");
            foreach (LibEffect effect in mine)
            {
                text.Append("  ").Append(effect.Id).Append("  ");
                if (effect.Error != null)
                {
                    text.Append("refused: ").Append(effect.Error).Append('\n');
                    continue;
                }
                text.Append(effect.IsShared ? "shared as " : "server only as ").Append(effect.Number).Append("  ").Append(effect.Kind);
                if (effect.Template != null)
                    text.Append(" from ").Append(effect.Template);
                if (effect.Kind == EffectKind.Code)
                    text.Append("; hits ").Append(effect.HitCount).Append(", ran ").Append(effect.HandlerRuns).Append(", errors ").Append(effect.HandlerErrors);
                foreach (string note in effect.Warnings)
                    text.Append("; ").Append(note);
                text.Append('\n');
            }
            foreach (LibEffect effect in lengths)
                text.Append("  ").Append(effect.Template).Append(" for ").Append(effect.Spec.Seconds).Append(" s  server only as ").Append(effect.Number).Append('\n');
            foreach (LibEffect effect in placeholders)
                text.Append("  ").Append(effect.Definition.name).Append("  ").Append(string.Join("; ", effect.Warnings.ToArray())).Append('\n');
        }

        internal static void Dump(StringBuilder text)
        {
            text.Append("== CookingLib effects (number, sent to players as, kind, stat, base, seconds, curve, glyph) ==\n");
            foreach (LibEffect effect in Effects.Live.OrderBy(e => e.Number))
            {
                EffectDefinition d = effect.Definition;
                text.Append(d.name).Append("  ").Append(effect.Number).Append("  ").Append(Effects.KeepOffWire(d) ? "filtered (0 without the filter)" : effect.Number.ToString(CultureInfo.InvariantCulture))
                    .Append("  ").Append(effect.Kind);
                StatDefinition stat = EffectFactory.StatOf(d);
                if (stat != null)
                    text.Append("  stat ").Append(stat.name).Append(' ').Append(stat.Hash);
                if (d is IndirectStatModifierDefinition)
                    text.Append("  base ").Append(Claims.Number(EffectFactory.BaseOf(d))).Append("  ").Append(EffectFactory.Seconds(d)).Append(" s  curve ").Append(EffectFactory.CurveOf(d));
                if (d is TeleportEffect)
                    text.Append("  to ").Append(EffectFactory.Destination(d)).Append("  chance x").Append(Claims.Number(EffectFactory.ChanceOf(d)));
                text.Append("  glyph ").Append(d.effectGlyph == null ? "none" : d.effectGlyph.name).Append('\n');
            }

            List<EffectDefinition> odd = HashedGeneralValue<EffectDefinition>.All.Where(e => e != null && e.Hash > 65535 && e.Hash % Effects.ServerStep != 0).ToList();
            text.Append("wire check: ").Append(odd.Count == 0 ? "no effect would reach players under another number" : string.Join(", ", odd.Select(e => e.name + " " + e.Hash + " arrives as " + (e.Hash & 0xFFFF)).ToArray())).Append("\n\n");

            text.Append("== Stats (number, the player's stat name, buff curve at no buff) ==\n");
            foreach (StatDefinition stat in HashedGeneralValue<StatDefinition>.All.Where(s => s != null).OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase))
            {
                float rest = stat.IndirectModificationCurve == null ? 1f : stat.IndirectModificationCurve.Evaluate(0f);
                text.Append(stat.name).Append("  ").Append(stat.Hash).Append("  ").Append(stat.Name).Append("  x").Append(Claims.Number(rest))
                    .Append(stat.IsIndirectUsageMultiplier && Math.Abs(rest - 1f) > 0.01f ? "  buff copies refused" : "").Append('\n');
            }
            text.Append("\n== Curves ==\n");
            foreach (Curve curve in HashedGeneralValue<Curve>.All.Where(c => c != null).OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase))
                text.Append(curve.name).Append("  ").Append(curve.Hash).Append('\n');
            text.Append('\n');
        }

        internal static string Stats(string name)
        {
            Player player = PlayerIds.Find(name);
            if (player == null)
                return "no player named '" + name + "' is online";
            EffectHandler handler = player.PlayerController == null ? null : player.PlayerController.GetComponentInChildren<EffectHandler>(true);
            StatManager manager = handler == null ? null : handler.StatManager;
            if (manager == null)
                return name + " has no stats yet";

            Dictionary<Stat, IndirectStatModifier> buffs = indirectField.GetValue(manager) as Dictionary<Stat, IndirectStatModifier>;
            StringBuilder text = new StringBuilder(player.UserInfo?.Username + ": stat, number, value, buff sum, buffs, seconds left\n");
            foreach (KeyValuePair<StatDefinition, Stat> pair in manager.Stats.OrderBy(p => p.Key == null ? "" : p.Key.name, StringComparer.OrdinalIgnoreCase))
            {
                if (pair.Key == null || pair.Value == null)
                    continue;
                text.Append(pair.Key.name).Append("  ").Append(pair.Key.Hash).Append("  ").Append(Claims.Number(pair.Value.Value));
                if (buffs != null && buffs.TryGetValue(pair.Value, out IndirectStatModifier buff) && buff != null)
                {
                    IEnumerable<IndirectModifier> tracked = (trackedField.GetValue(buff) as IEnumerable<IndirectModifier>) ?? Enumerable.Empty<IndirectModifier>();
                    List<long> left = tracked.Select(m => (long)(uint)durationField.GetValue(m) - (uint)tickField.GetValue(m)).OrderBy(x => x).ToList();
                    text.Append("  buff ").Append(Claims.Number((float)sumField.GetValue(buff))).Append("  x").Append(left.Count);
                    if (left.Count > 0)
                        text.Append("  ").Append(string.Join(" ", left.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray()));
                }
                text.Append('\n');
            }
            return text.ToString();
        }
    }
}
