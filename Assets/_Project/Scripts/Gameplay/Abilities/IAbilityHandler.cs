namespace Lichronicle.Gameplay.Abilities
{
    public interface IAbilityHandler
    {
        void OnAbilityTriggered(AbilityDefinition definition, bool active);
    }
}

