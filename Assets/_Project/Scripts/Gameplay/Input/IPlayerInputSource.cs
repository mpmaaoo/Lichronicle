using UnityEngine;

namespace Lichronicle.Gameplay.Inputs
{
    public interface IPlayerInputSource
    {
        int Priority { get; }

        float GetAxisRaw(string axisName);

        bool GetButtonDown(string buttonName);

        bool GetKeyDown(KeyCode keyCode);
    }
}
