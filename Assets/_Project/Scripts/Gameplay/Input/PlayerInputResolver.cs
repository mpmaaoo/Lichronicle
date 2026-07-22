using UnityEngine;

namespace Lichronicle.Gameplay.Inputs
{
    public static class PlayerInputResolver
    {
        public static IPlayerInputSource Resolve(GameObject owner)
        {
            if (owner == null)
                return null;

            var behaviours = owner.GetComponents<MonoBehaviour>();

            IPlayerInputSource best = null;
            var bestPriority = int.MinValue;

            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];

                if (behaviour == null || !behaviour.isActiveAndEnabled)
                    continue;

                if (behaviour is not IPlayerInputSource source)
                    continue;

                if (source.Priority > bestPriority)
                {
                    best = source;
                    bestPriority = source.Priority;
                }
            }

            return best;
        }
    }
}
