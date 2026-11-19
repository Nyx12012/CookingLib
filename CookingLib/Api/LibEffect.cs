using System.Collections.Generic;
using Alta.Alchemy;

namespace CookingLib
{
    /// <summary>An effect a mod registered. Check Error before using it.</summary>
    public class LibEffect
    {
        internal LibEffect(CustomEffectSpec spec, bool isShared, uint number)
        {
            Spec = spec;
            IsShared = isShared;
            Number = number;
        }

        /// <summary>The spec it was registered with.</summary>
        public CustomEffectSpec Spec { get; }

        /// <summary>The id from the spec.</summary>
        public string Id => Spec?.Id;

        /// <summary>What it does.</summary>
        public EffectKind Kind { get; internal set; }

        /// <summary>True for a shared effect, which every player needs CookingLib and your mod to read.</summary>
        public bool IsShared { get; }

        /// <summary>Its number in saves: 1 to 65535 for a shared effect, a multiple of 65536 for a server only one, which players never receive.</summary>
        public uint Number { get; internal set; }

        /// <summary>Why it was refused, or null when it registered.</summary>
        public string Error { get; internal set; }

        /// <summary>The game effect it registered as. Null when refused.</summary>
        public EffectDefinition Definition { get; internal set; }

        /// <summary>Things worth knowing, such as a small buff on a stat players read themselves.</summary>
        public IReadOnlyList<string> Warnings => Notes;

        internal List<string> Notes { get; } = new List<string>();

        internal string Template { get; set; }

        internal int HitCount { get; set; }

        internal int HandlerRuns { get; set; }

        internal int HandlerErrors { get; set; }

        internal bool Hidden { get; set; }
    }
}
