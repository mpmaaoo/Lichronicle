using UnityEngine;
using UnityEngine.UI;
using Lichronicle.Gameplay.PhantomRecord;

namespace Lichronicle.UI
{
    public sealed class PhantomRecordStatusUI : MonoBehaviour
    {
        [SerializeField] private PhantomOperationRecorder recorder;

        [Header("自動建立 UI")]
        [SerializeField] private bool autoCreateUI = true;
        [SerializeField] private Vector2 anchoredPosition = new Vector2(20f, -20f);
        [SerializeField] private Vector2 size = new Vector2(260f, 70f);

        private Text _statusText;

        private void Awake()
        {
            if (autoCreateUI && _statusText == null)
                CreateUI();
        }

        private void Update()
        {
            if (recorder == null || _statusText == null)
                return;

            if (recorder.IsRecording)
            {
                _statusText.text =
                    $"REC\n{recorder.CurrentRecordTime:0.0}/{recorder.RecordDuration:0.0}s";
                return;
            }

            if (recorder.HasActiveClone)
            {
                _statusText.text = $"CLONE\nx{recorder.ActiveCloneCount}";
                return;
            }

            if (recorder.HasRecord)
            {
                _statusText.text = $"READY\nFrames: {recorder.RecordedFrameCount}";
                return;
            }

            _statusText.text = "IDLE";
        }

        private void CreateUI()
        {
            var canvasObject = new GameObject("PhantomRecordStatusCanvas");
            canvasObject.transform.SetParent(transform, false);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            var panelObject = new GameObject("StatusPanel");
            panelObject.transform.SetParent(canvasObject.transform, false);

            var rect = panelObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = panelObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.65f);

            var textObject = new GameObject("StatusText");
            textObject.transform.SetParent(panelObject.transform, false);

            var textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 8f);
            textRect.offsetMax = new Vector2(-10f, -8f);

            _statusText = textObject.AddComponent<Text>();
            _statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _statusText.fontSize = 22;
            _statusText.alignment = TextAnchor.MiddleLeft;
            _statusText.color = Color.white;
            _statusText.text = "IDLE";
        }
    }
}
