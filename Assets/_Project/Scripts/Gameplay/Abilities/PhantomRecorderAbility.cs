using System.Collections.Generic;
using UnityEngine;

namespace Lichronicle.Gameplay.Abilities
{
    /// <summary>
    /// 技能B：幻影紀錄（MVP）
    /// 目前版本：
    /// - 開啟時開始記錄玩家一段時間內的位置與朝向。
    /// - 關閉時在同一個路徑上生成一個「幻影」物件，循環播放這段軌跡。
    /// 未來可以改成記錄更細緻的操作（按鍵、技能），此類邏輯都集中在這個 Component。
    /// </summary>
    public sealed class PhantomRecorderAbility : MonoBehaviour, IAbilityHandler
    {
        [SerializeField] private Transform playerRoot;
        [SerializeField] private GameObject phantomPrefab;
        [SerializeField] private float recordDuration = 5f;
        [SerializeField] private float sampleInterval = 0.05f;

        private readonly List<PoseSample> _samples = new();
        private float _recordTimer;
        private float _sampleTimer;
        private bool _recording;

        private void Update()
        {
            if (!_recording || playerRoot == null)
                return;

            _recordTimer += Time.deltaTime;
            _sampleTimer += Time.deltaTime;

            if (_sampleTimer >= sampleInterval)
            {
                _sampleTimer = 0f;
                _samples.Add(new PoseSample
                {
                    position = playerRoot.position,
                    rotation = playerRoot.rotation
                });
            }

            if (_recordTimer >= recordDuration)
            {
                StopAndSpawnPhantom();
            }
        }

        public void OnAbilityTriggered(AbilityDefinition definition, bool active)
        {
            if (definition == null || definition.kind != AbilityKind.PhantomRecord)
                return;

            if (active)
            {
                StartRecording();
            }
            else
            {
                StopAndSpawnPhantom();
            }
        }

        private void StartRecording()
        {
            if (playerRoot == null)
                return;

            _samples.Clear();
            _recordTimer = 0f;
            _sampleTimer = 0f;
            _recording = true;
        }

        private void StopAndSpawnPhantom()
        {
            if (!_recording)
                return;

            _recording = false;

            if (phantomPrefab == null || _samples.Count == 0)
                return;

            var phantom = Instantiate(phantomPrefab, _samples[0].position, _samples[0].rotation);
            var playback = phantom.GetComponent<PhantomPlayback>();
            if (playback == null)
                playback = phantom.AddComponent<PhantomPlayback>();

            playback.SetPath(_samples, sampleInterval);
        }

        public struct PoseSample
        {
            public Vector3 position;
            public Quaternion rotation;
        }
    }
}

