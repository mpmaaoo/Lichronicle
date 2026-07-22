using System.Collections.Generic;
using UnityEngine;

namespace Lichronicle.Gameplay.Abilities
{
    /// <summary>
    /// 幻影重播：沿著紀錄的路徑無限循環移動。
    /// </summary>
    public sealed class PhantomPlayback : MonoBehaviour
    {
        private readonly List<PhantomRecorderAbility.PoseSample> _samples = new();
        private float _interval;
        private float _timer;
        private int _index;

        public void SetPath(IEnumerable<PhantomRecorderAbility.PoseSample> samples, float interval)
        {
            _samples.Clear();
            _samples.AddRange(samples);
            _interval = interval;
            _timer = 0f;
            _index = 0;

            if (_samples.Count > 0)
            {
                transform.position = _samples[0].position;
                transform.rotation = _samples[0].rotation;
            }
        }

        private void Update()
        {
            if (_samples.Count == 0)
                return;

            _timer += Time.deltaTime;
            if (_timer < _interval)
                return;

            _timer -= _interval;
            _index = (_index + 1) % _samples.Count;
            var s = _samples[_index];
            transform.position = s.position;
            transform.rotation = s.rotation;
        }
    }
}

