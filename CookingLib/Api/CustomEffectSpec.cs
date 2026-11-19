using System;

namespace CookingLib
{
    /// <summary>A new effect. Give either a Template, the game effect it copies, or a Handler alone. Fields left null keep the template's values.</summary>
    public class CustomEffectSpec
    {
        /// <summary>Unique id written as author.thing. Never rename it: saves keep the effect's number.</summary>
        public string Id;

        /// <summary>The game effect to copy: a buff such as Speed, a stat change such as Heal, a teleport, or Inate for a marker. Copied from the dump.</summary>
        public string Template;

        /// <summary>Buff and Change: the stat it acts on, by the dump's stat name or number.</summary>
        public string Stat;

        /// <summary>Buff: how much one sip at strength 1 adds to the stat's buff.</summary>
        public float? BaseValue;

        /// <summary>Buff: how long it lasts, 1 to 86400 seconds. Game buffs last 900.</summary>
        public int? Seconds;

        /// <summary>Teleport: where it sends the player, a spawn area name such as Home or OutsideLake.</summary>
        public string Destination;

        /// <summary>Teleport: multiplies the chance, above 0.</summary>
        public float? Chance;

        /// <summary>Code effect: runs on the server from the next frame, once per sip, bite or splash, with the hits of one frame merged.</summary>
        public Action<EffectHit> Handler;

        /// <summary>The glyph a journal page shows for it, a game glyph from the dump. Copies keep the template's; anything on a page needs one.</summary>
        public string Glyph;
    }
}
