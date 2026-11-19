using Alta.Liquid;
using UnityEngine;

namespace CookingLib
{
    /// <summary>Colours as text, and the colours players see.</summary>
    public static class Colors
    {
        /// <summary>A colour from #RRGGBB, #RRGGBBAA or a name such as red, teal, orange or grey. Null when it is not one.</summary>
        public static Color? Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;
            return ColorUtility.TryParseHtmlString(text.Trim(), out Color color) ? color : (Color?)null;
        }

        /// <summary>A liquid look's own colour, the brightest a liquid with that look can be. Null for an unknown look.</summary>
        public static Color? OfLook(string look)
        {
            LiquidVisualData found = Names.Find<LiquidVisualData>(look);
            return found == null ? (Color?)null : found.LiquidColor;
        }

        /// <summary>The colour players see for a game or CookingLib liquid: its tint times its look. Null for an unknown liquid.</summary>
        public static Color? OfLiquid(string liquid)
        {
            LibLiquid ours = Liquids.Get(liquid);
            LiquidData data = ours != null && ours.Error == null ? ours.Data : Names.Find<LiquidDefinition>(liquid)?.Data;
            if (data == null || data.LiquidVisual == null)
                return null;
            return data.TintedLiquidColor;
        }
    }
}
