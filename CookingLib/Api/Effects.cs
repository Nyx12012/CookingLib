using System;
using System.Collections.Generic;
using System.Linq;
using Alta;
using Alta.Alchemy;
using UnityEngine;

namespace CookingLib
{
    /// <summary>New effects that liquids, stews, ingredients and foods can carry by id. Register them from Main.PreSetup. Nothing is registered by default.</summary>
    public static class Effects
    {
        internal const uint ServerStep = 65536;
        internal const int ServerSlots = 32767;

        static readonly List<LibEffect> all = new List<LibEffect>();
        static readonly List<LibEffect> hidden = new List<LibEffect>();
        static readonly Dictionary<int, LibEffect> byDefinition = new Dictionary<int, LibEffect>();
        static readonly Dictionary<string, LibEffect> seconds = new Dictionary<string, LibEffect>(StringComparer.Ordinal);
        static readonly Dictionary<long, EffectHit> pending = new Dictionary<long, EffectHit>();
        static readonly List<EffectHit> ready = new List<EffectHit>();

        /// <summary>Every effect mods registered, refused ones included.</summary>
        public static IReadOnlyList<LibEffect> All => all;

        /// <summary>Why the effect patches are off, or null.</summary>
        public static string Error { get; internal set; }

        /// <summary>A new effect only the server knows. Players never receive it, so vanilla players can join. A modded player's CookingLib registers it too, so pages can show its glyph. Call it from Main.PreSetup. Never throws.</summary>
        public static LibEffect RegisterServerOnly(CustomEffectSpec spec) => Register(spec, false, 0);

        /// <summary>A new effect under a number from 1 to 65535 that no other mod uses. Call it from Main.PreSetup on the server and on players' machines with the same number and spec. Players without it log one error each time a liquid carrying it reaches them. Never throws.</summary>
        public static LibEffect RegisterShared(uint number, CustomEffectSpec spec) => Register(spec, true, number);

        /// <summary>The registered effect with this id, or null.</summary>
        public static LibEffect Get(string id) => all.FirstOrDefault(e => e.Error == null && string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

        internal static IReadOnlyList<LibEffect> Hidden => hidden;

        internal static IEnumerable<LibEffect> Live => all.Concat(hidden).Where(e => e.Error == null && e.Definition != null);

        internal static int Filtered { get; set; }

        internal static int DroppedOnLoad { get; set; }

        internal static bool Any => Live.Any();

        internal static bool Booked => Main.IsServer && Safe.Run("effect book", () => EffectBook.All.Count > 0, false);

        internal static EffectKinds KindOf(EffectDefinition effect)
        {
            if (effect == null || !byDefinition.TryGetValue(effect.GetInstanceID(), out LibEffect entry))
                return EffectKinds.None;
            if (entry.Kind == EffectKind.Tag && entry.Template == null)
                return EffectKinds.Placeholder;
            return entry.IsShared ? EffectKinds.Shared : EffectKinds.ServerOnly;
        }

        internal static LibEffect Find(EffectDefinition effect) =>
            effect != null && byDefinition.TryGetValue(effect.GetInstanceID(), out LibEffect entry) ? entry : null;

        internal static bool KeepOffWire(EffectDefinition effect)
        {
            if (effect == null)
                return false;
            EffectKinds kind = KindOf(effect);
            return kind == EffectKinds.ServerOnly || kind == EffectKinds.Placeholder;
        }

        internal static uint ServerNumber(string id)
        {
            uint hash = 2166136261;
            foreach (char c in (id ?? "").ToLowerInvariant())
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (1 + hash % ServerSlots) * ServerStep;
        }

        static LibEffect Register(CustomEffectSpec spec, bool shared, uint number)
        {
            LibEffect entry = new LibEffect(spec, shared, shared ? number : ServerNumber(spec?.Id));
            try
            {
                if (spec == null)
                    throw new ArgumentException("spec is null");
                if (!Main.InPreSetup)
                    throw new ArgumentException("register effects from Main.PreSetup, before liquids and pages are built");
                CheckId(spec.Id);
                if (shared && (number < 1 || number > 65535))
                    throw new ArgumentException("a shared effect's number must be 1 to 65535");
                CheckNumber(entry.Number, spec.Id);
                EffectDefinition effect = EffectFactory.Build(entry);
                if (!Registry.Add<EffectDefinition>(effect))
                    throw new ArgumentException("number " + entry.Number + " is taken");
                entry.Definition = effect;
                byDefinition[effect.GetInstanceID()] = entry;
                if (Main.IsServer)
                    Safe.Run("effect book", () => EffectBook.Book(entry.Number, spec.Id, entry.Kind.ToString()));
                Warn(entry);
            }
            catch (Exception e)
            {
                entry.Error = e is ArgumentException ? e.Message : e.ToString();
            }

            all.Add(entry);
            if (entry.Error != null)
                Log.Warn("effect " + (spec?.Id ?? "null") + " refused: " + entry.Error);
            else
                Log.Info("effect " + spec.Id + (shared ? " shared as " : " server only as ") + entry.Number + ", " + entry.Kind + (entry.Template == null ? "" : " from " + entry.Template));
            return entry;
        }

        static void CheckId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOf('.') <= 0 || id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
                throw new ArgumentException("id must look like author.thing, with no spaces");
            if (all.Any(e => e.Error == null && string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("id is already used");
            string key = Names.Norm(id);
            EffectDefinition same = HashedGeneralValue<EffectDefinition>.All.FirstOrDefault(e => e != null && Names.Norm(e.name) == key);
            if (same != null)
                throw new ArgumentException("id matches the effect " + same.name);
        }

        static void CheckNumber(uint number, string id)
        {
            EffectDefinition taken = Registry.Find<EffectDefinition>(number);
            if (taken != null)
            {
                LibEffect other = Find(taken);
                throw new ArgumentException("number " + number + " is taken by " + (other != null ? other.Id : taken.name) + (number % ServerStep == 0 ? ", rename the id" : ""));
            }
            string owner = Main.IsServer ? Safe.Run("effect book", () => EffectBook.Owner(number), null) : null;
            if (owner != null && !string.Equals(owner, id, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("number " + number + " belongs to " + owner + " in this server's effect book (" + EffectBook.FileName + "), saves may hold it");
        }

        static void Warn(LibEffect entry)
        {
            EffectDefinition effect = entry.Definition;
            if (entry.Kind == EffectKind.Buff && !entry.IsShared && EffectFactory.BaseOf(effect) < 0.5f)
            {
                string stat = EffectFactory.StatOf(effect)?.name ?? "";
                if (stat.IndexOf("speed", StringComparison.OrdinalIgnoreCase) >= 0 || stat.IndexOf("lumin", StringComparison.OrdinalIgnoreCase) >= 0)
                    entry.Notes.Add("a small buff on " + stat + " may not reach players until the stat changes enough to sync");
            }
            if (effect.effectGlyph == null)
                entry.Notes.Add("no glyph, so no journal page can show it");
        }

        internal static EffectDefinition WithSeconds(EffectDefinition template, int length, EffectKinds allowed)
        {
            if ((allowed & EffectKinds.ServerOnly) == 0)
                throw new ArgumentException("Seconds makes a server only copy of " + template.name + ", which only server side liquids, stews and changes can carry");
            string id = "cookinglib.seconds." + template.Hash + "." + length;
            if (seconds.TryGetValue(id, out LibEffect known))
            {
                if (known.Error != null)
                    throw new ArgumentException(known.Error);
                return known.Definition;
            }

            LibEffect entry = new LibEffect(new CustomEffectSpec { Id = id, Template = template.name, Seconds = length }, false, ServerNumber(id)) { Kind = EffectKind.Buff, Template = template.name, Hidden = true };
            seconds[id] = entry;
            try
            {
                CheckNumber(entry.Number, id);
                EffectDefinition copy = EffectFactory.SecondsCopy(template, length, entry.Number, id);
                if (!Registry.Add<EffectDefinition>(copy))
                    throw new ArgumentException("number " + entry.Number + " is taken");
                entry.Definition = copy;
                byDefinition[copy.GetInstanceID()] = entry;
                if (Main.IsServer)
                    Safe.Run("effect book", () => EffectBook.Book(entry.Number, id, "Seconds"));
                Log.Info("effect " + template.name + " for " + length + " s made as " + entry.Number);
            }
            catch (Exception e)
            {
                entry.Error = e is ArgumentException ? e.Message : e.ToString();
            }
            hidden.Add(entry);
            if (entry.Error != null)
                throw new ArgumentException(entry.Error);
            return entry.Definition;
        }

        internal static void AfterPostSetup(HarmonyLib.Harmony harmony)
        {
            if (!Main.IsServer)
                return;
            int made = Safe.Run("effect placeholders", Placeholders, 0);
            if (made > 0)
                Log.Info("effects: " + made + " placeholder" + (made == 1 ? "" : "s") + " for numbers in the effect book no mod registered this time");
            if (Any || Booked)
                WirePatchOn = EffectWirePatch.Apply(harmony);
        }

        internal static bool WirePatchOn { get; private set; }

        static int Placeholders()
        {
            int made = 0;
            foreach (EffectBook.Entry booked in EffectBook.Missing(Live.Select(e => e.Number)).ToList())
            {
                if (Registry.Find<EffectDefinition>(booked.Number) != null || Remake(booked))
                    continue;
                InateEffect placeholder = ScriptableObject.CreateInstance<InateEffect>();
                placeholder.name = "cookinglib placeholder " + booked.Number;
                placeholder.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Registry.SetHash(placeholder, booked.Number);
                if (!Registry.Add<EffectDefinition>(placeholder))
                    continue;
                LibEffect entry = new LibEffect(new CustomEffectSpec { Id = placeholder.name }, booked.Number < ServerStep, booked.Number)
                {
                    Kind = EffectKind.Tag,
                    Definition = placeholder,
                    Hidden = true
                };
                entry.Notes.Add("stands in for " + booked.Id + " (" + booked.Kind + "), which no mod registered this time");
                byDefinition[placeholder.GetInstanceID()] = entry;
                hidden.Add(entry);
                made++;
            }
            return made;
        }

        static bool Remake(EffectBook.Entry booked)
        {
            string[] parts = booked.Id.Split('.');
            if (parts.Length != 4 || !booked.Id.StartsWith("cookinglib.seconds.", StringComparison.Ordinal)
                || !uint.TryParse(parts[2], out uint template) || !int.TryParse(parts[3], out int length))
                return false;
            EffectDefinition game = Registry.Find<EffectDefinition>(template);
            if (game == null || EffectNames.KindOf(game) != EffectKinds.Game)
                return false;
            return Safe.Run("effect length " + booked.Id, () => WithSeconds(game, length, EffectKinds.ServerOnly), null) != null;
        }

        internal static void Hit(LibEffect effect, EffectHandler target, float strength)
        {
            try
            {
                if (effect == null || effect.Spec.Handler == null || !Main.IsServer)
                    return;
                effect.HitCount++;
                long key = ((long)effect.Number << 32) ^ (uint)(target == null ? 0 : target.GetInstanceID());
                if (!pending.TryGetValue(key, out EffectHit hit))
                {
                    PlayerEffectController controller = target as PlayerEffectController;
                    Alta.Character.PlayerCharacter character = controller == null ? null : controller.Player;
                    Player player = character == null ? null : character.NetworkPlayer;
                    hit = new EffectHit(effect, target, player, PlayerIds.InfoOf(player));
                    pending[key] = hit;
                }
                hit.Strength += strength;
                hit.Hits++;
            }
            catch (Exception e)
            {
                Safe.Fail("code effect", e);
            }
        }

        internal static void DrainHits()
        {
            if (pending.Count == 0)
                return;
            ready.Clear();
            ready.AddRange(pending.Values);
            pending.Clear();
            foreach (EffectHit hit in ready)
            {
                LibEffect effect = hit.Effect;
                effect.HandlerRuns++;
                try
                {
                    effect.Spec.Handler(hit);
                }
                catch (Exception e)
                {
                    effect.HandlerErrors++;
                    if (effect.HandlerErrors <= 3)
                        Log.Error("effect " + effect.Id + " handler failed: " + e);
                }
            }
        }
    }
}
