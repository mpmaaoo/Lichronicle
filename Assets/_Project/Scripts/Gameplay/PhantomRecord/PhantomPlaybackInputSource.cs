using System;
using System.Collections.Generic;
using UnityEngine;
using Lichronicle.Gameplay.Inputs;

namespace Lichronicle.Gameplay.PhantomRecord
{
    [DefaultExecutionOrder(-500)]
    public sealed class PhantomPlaybackInputSource : MonoBehaviour, IPlayerInputSource
    {
        public int Priority => 100;

        [Header("回放設定")]
        [SerializeField] private bool replayTransform = true;
        [SerializeField] private bool replayPosition = true;
        [SerializeField] private bool replayRotation = true;
        [SerializeField] private bool interpolateBetweenFrames = true;

        [Header("物理移動設定")]
        [SerializeField] private bool useRigidbodyMovePosition = true;

        [Header("地面抖動修正")]
        [SerializeField] private bool filterSmallYJitter = true;
        [SerializeField, Min(0f)] private float yJitterThreshold = 0.05f;

        private readonly List<RecordedPlayerFrame> _frames = new();

        private Rigidbody _rigidbody;

        private float _timer;
        private float _frameStartTime;
        private float _frameEndTime;

        private int _index;
        private bool _playing;

        private int _currentFrameStartedUnityFrame;

        private bool _hasStableY;
        private float _stableY;

        private Action<PhantomPlaybackInputSource> _onFinished;

        public bool IsPlaying => _playing;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (_rigidbody != null)
            {
                _rigidbody.useGravity = false;
                _rigidbody.isKinematic = true;
                _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
        }

        public void BeginPlayback(
            IReadOnlyList<RecordedPlayerFrame> frames,
            Action<PhantomPlaybackInputSource> onFinished)
        {
            _frames.Clear();

            for (var i = 0; i < frames.Count; i++)
                _frames.Add(frames[i]);

            _onFinished = onFinished;

            _timer = 0f;
            _index = 0;
            _playing = _frames.Count > 0;

            _frameStartTime = 0f;
            _frameEndTime = _playing ? Mathf.Max(0.0001f, _frames[0].deltaTime) : 0f;

            _currentFrameStartedUnityFrame = Time.frameCount;

            _hasStableY = false;

            if (_playing)
            {
                var first = _frames[0];

                _stableY = first.worldPosition.y;
                _hasStableY = true;

                ApplyTransformImmediately(first.worldPosition, first.worldRotation);
            }
            else
            {
                FinishPlayback();
            }
        }

        private void Update()
        {
            if (!_playing)
                return;

            _timer += Time.deltaTime;

            while (_playing && _timer >= _frameEndTime)
            {
                _index++;

                if (_index >= _frames.Count)
                {
                    FinishPlayback();
                    return;
                }

                _frameStartTime = _frameEndTime;
                _frameEndTime += Mathf.Max(0.0001f, _frames[_index].deltaTime);
                _currentFrameStartedUnityFrame = Time.frameCount;
            }
        }

        private void FixedUpdate()
        {
            if (!_playing)
                return;

            if (!replayTransform || _frames.Count == 0)
                return;

            GetCurrentPlaybackTransform(out var position, out var rotation);

            if (filterSmallYJitter)
                position = FilterYJitter(position);

            ApplyTransformPhysics(position, rotation);
        }

        private void GetCurrentPlaybackTransform(out Vector3 position, out Quaternion rotation)
        {
            var currentIndex = Mathf.Clamp(_index, 0, _frames.Count - 1);
            var current = _frames[currentIndex];

            if (!interpolateBetweenFrames || currentIndex >= _frames.Count - 1)
            {
                position = current.worldPosition;
                rotation = current.worldRotation;
                return;
            }

            var next = _frames[currentIndex + 1];

            var duration = Mathf.Max(0.0001f, _frameEndTime - _frameStartTime);
            var t = Mathf.Clamp01((_timer - _frameStartTime) / duration);

            position = Vector3.Lerp(current.worldPosition, next.worldPosition, t);
            rotation = Quaternion.Slerp(current.worldRotation, next.worldRotation, t);
        }

        private Vector3 FilterYJitter(Vector3 position)
        {
            if (!_hasStableY)
            {
                _stableY = position.y;
                _hasStableY = true;
                return position;
            }

            var yDifference = Mathf.Abs(position.y - _stableY);

            if (yDifference <= yJitterThreshold)
            {
                position.y = _stableY;
                return position;
            }

            _stableY = position.y;
            return position;
        }

        private void ApplyTransformImmediately(Vector3 position, Quaternion rotation)
        {
            if (replayPosition)
            {
                transform.position = position;

                if (_rigidbody != null)
                    _rigidbody.position = position;
            }

            if (replayRotation)
            {
                transform.rotation = rotation;

                if (_rigidbody != null)
                    _rigidbody.rotation = rotation;
            }
        }

        private void ApplyTransformPhysics(Vector3 position, Quaternion rotation)
        {
            if (_rigidbody != null && useRigidbodyMovePosition)
            {
                if (replayPosition)
                    _rigidbody.MovePosition(position);

                if (replayRotation)
                    _rigidbody.MoveRotation(rotation);

                return;
            }

            if (replayPosition)
                transform.position = position;

            if (replayRotation)
                transform.rotation = rotation;
        }

        public float GetAxisRaw(string axisName)
        {
            if (!_playing || _frames.Count == 0)
                return 0f;

            var frame = _frames[Mathf.Clamp(_index, 0, _frames.Count - 1)];

            if (axisName == "Horizontal")
                return frame.horizontal;

            if (axisName == "Vertical")
                return frame.vertical;

            return 0f;
        }

        public bool GetButtonDown(string buttonName)
        {
            if (!_playing || _frames.Count == 0)
                return false;

            if (buttonName != "Jump")
                return false;

            if (Time.frameCount != _currentFrameStartedUnityFrame)
                return false;

            var frame = _frames[Mathf.Clamp(_index, 0, _frames.Count - 1)];
            return frame.jumpDown;
        }

        public bool GetKeyDown(KeyCode keyCode)
        {
            if (!_playing || _frames.Count == 0 || keyCode == KeyCode.None)
                return false;

            if (Time.frameCount != _currentFrameStartedUnityFrame)
                return false;

            var frame = _frames[Mathf.Clamp(_index, 0, _frames.Count - 1)];

            return frame.keyDowns != null && frame.keyDowns.Contains(keyCode);
        }

        private void FinishPlayback()
        {
            if (!_playing)
                return;

            _playing = false;
            _onFinished?.Invoke(this);
        }
    }
}
