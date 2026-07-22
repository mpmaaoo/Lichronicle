using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lichronicle.Gameplay.PhantomRecord
{
    [Serializable]
    public struct RecordedPlayerFrame
    {
        public float deltaTime;

        public Vector3 worldPosition;
        public Quaternion worldRotation;

        public float horizontal;
        public float vertical;
        public bool jumpDown;

        public List<KeyCode> keyDowns;
    }
}
