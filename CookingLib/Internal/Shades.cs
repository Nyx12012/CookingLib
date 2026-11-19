using System;
using System.Collections.Generic;
using System.Linq;
using Alta.Cooking;
using Alta.Liquid;
using UnityEngine;

namespace CookingLib
{
    internal static class Shades
    {
        internal static readonly uint[] Candidates = { 21644, 20952, 55814, 7838 };

        const float GoodEnough = 0.1f;

        internal static Color Need(string text, string field)
        {
            Color? color = Colors.Parse(text);
            if (!color.HasValue)
                throw new ArgumentException(field + " '" + text + "' is not a colour: use #RRGGBB or a name such as red, teal or orange");
            return color.Value;
        }

        internal static Color Divide(Color want, Color look, List<string> clamped)
        {
            return new Color(Channel(want.r, look.r, "red", clamped), Channel(want.g, look.g, "green", clamped), Channel(want.b, look.b, "blue", clamped), Mathf.Clamp01(want.a));
        }

        static float Channel(float want, float look, string name, List<string> clamped)
        {
            if (look <= 0.0001f)
            {
                if (want > 0.004f && clamped != null)
                    clamped.Add(name + " lost, the look has none");
                return want > 0f ? 1f : 0f;
            }
            float tint = want / look;
            if (tint > 1.004f && clamped != null)
                clamped.Add(name + " capped at " + look.ToString("0.###") + " by the look");
            return Mathf.Clamp01(tint);
        }

        internal static LiquidVisualData Pick(Color want)
        {
            LiquidVisualData best = null;
            float bestLoss = float.MaxValue;
            foreach (uint hash in Candidates)
            {
                LiquidVisualData look = Registry.Find<LiquidVisualData>(hash);
                if (look == null)
                    continue;
                float loss = Loss(want, look.LiquidColor);
                if (loss <= GoodEnough)
                    return look;
                if (loss < bestLoss - 0.0001f)
                {
                    best = look;
                    bestLoss = loss;
                }
            }
            if (best == null)
                throw new ArgumentException("none of the looks a Shade picks from loaded, set Look");
            return best;
        }

        internal static float Loss(Color want, Color look) =>
            Mathf.Max(0f, want.r - look.r) + Mathf.Max(0f, want.g - look.g) + Mathf.Max(0f, want.b - look.b);

        internal static Color Times(Color tint, Color look) => new Color(tint.r * look.r, tint.g * look.g, tint.b * look.b, tint.a * look.a);

        internal static string Text(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c) + " (" + Claims.Number(c.r) + "/" + Claims.Number(c.g) + "/" + Claims.Number(c.b) + ")";

        internal static Color OnStage(Color want, StewComponent stew)
        {
            Color look = stew == null || stew.LiquidVisualData == null ? Color.white : stew.LiquidVisualData.LiquidColor;
            return Divide(want, look, null);
        }

        internal static IEnumerable<LiquidVisualData> CandidateLooks() => Candidates.Select(h => Registry.Find<LiquidVisualData>(h)).Where(l => l != null);
    }
}
