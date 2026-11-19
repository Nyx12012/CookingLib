using Alta.Alchemy;

namespace CookingLib
{
    internal sealed class CookingLibEffect : EffectDefinition
    {
        internal LibEffect Owner;

        protected override void Apply(EffectHandler target, float strength)
        {
            Effects.Hit(Owner, target, strength);
        }
    }
}
