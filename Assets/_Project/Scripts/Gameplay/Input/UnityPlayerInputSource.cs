using UnityEngine;

namespace Lichronicle.Gameplay.Inputs
{
    public sealed class UnityPlayerInputSource : MonoBehaviour, IPlayerInputSource
    {
        public int Priority => 0;

        public float GetAxisRaw(string axisName)
        {
            return Input.GetAxisRaw(axisName);
        }

        public bool GetButtonDown(string buttonName)
        {
            return Input.GetButtonDown(buttonName);
        }

        public bool GetKeyDown(KeyCode keyCode)
        {
            if (keyCode == KeyCode.None)
                return false;

            return Input.GetKeyDown(keyCode);
        }
    }
}
