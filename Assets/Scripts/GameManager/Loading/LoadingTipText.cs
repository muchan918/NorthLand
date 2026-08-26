using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;

namespace NorthLand.Core
{
    /// <summary>
    /// 로딩 문구를 무작위로 하나 골라 띄우고, 분홍 액체가 글자에 차오르는 연출을 입힌다.
    /// <b>진행률 바를 대신하는 표시물</b>이라, 차오르는 높이가 곧 진행률이다.
    ///
    /// <b>두 겹 구조.</b> 같은 문구를 그리는 TMP 두 개를 겹친다 — 아래쪽(<see cref="baseText"/>)이
    /// "아직 안 찬" 글자, 위쪽(<see cref="fillText"/>)이 그라디언트를 입은 "찬" 글자다.
    /// 찬 쪽은 바닥에 고정된 <see cref="fillMask"/>(RectMask2D) 안에 들어 있고, 그 마스크의 높이만
    /// 움직인다. 셰이더를 새로 쓰지 않고 TMP 기본 머티리얼만으로 성립하는 것이 이 구조의 이유다.
    ///
    /// ⚠ 두 TMP는 <b>글꼴·크기·정렬·사각형 높이가 같아야</b> 글자가 정확히 겹친다.
    /// 그래서 <see cref="fillText"/>의 높이를 매 프레임 <see cref="baseText"/>에서 받아 맞춘다 —
    /// 문구 길이에 따라 레이아웃(VerticalLayoutGroup + ContentSizeFitter)이 높이를 바꾸기 때문이다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class LoadingTipText : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("아직 차지 않은 글자. 레이아웃 행이자 LocalizeStringEvent가 문자열을 넣는 대상이다.")]
        [SerializeField]
        private TMP_Text baseText;

        [Tooltip("차오르는 글자. 그라디언트를 입고 fillMask 안에 들어간다.")]
        [SerializeField]
        private TMP_Text fillText;

        [Tooltip("바닥 고정 RectMask2D의 RectTransform. 이 높이만 움직인다.")]
        [SerializeField]
        private RectTransform fillMask;

        [SerializeField]
        private LocalizeStringEvent localizeStringEvent;

        [Header("Tips")]
        [SerializeField]
        private string tableCollection = LocalizationHelper.k_DefaultTable;

        [Tooltip("이 중 하나를 시작할 때 무작위로 고른다. String Table의 키를 그대로 적는다.")]
        [SerializeField]
        private List<string> tipKeys = new List<string>();

        [Header("Fill")]
        [Tooltip("액체 표면(위) 색.")]
        [SerializeField]
        private Color fillTop = new Color32(0xFF, 0xD2, 0xC8, 0xFF);

        [Tooltip("액체 바닥(아래) 색.")]
        [SerializeField]
        private Color fillBottom = new Color32(0xFF, 0x78, 0x96, 0xFF);

        [Tooltip("표면이 넘실거리는 폭(픽셀). 0이면 딱 잘린 수평선이 되어 액체로 안 읽힌다.")]
        [SerializeField]
        [Min(0f)]
        private float surfaceWavePixels = 2.5f;

        [Tooltip("표면이 넘실거리는 속도(초당 왕복 횟수).")]
        [SerializeField]
        [Min(0f)]
        private float surfaceWaveSpeed = 1.1f;

        /// 로딩 흐름이 보고한 목표 채움 비율.
        private float fillAmount;

        private void Awake()
        {
            ApplyGradient();
            PickRandomTip();
            ApplyFill();
        }

        private void OnDestroy()
        {
            if (localizeStringEvent != null)
            {
                localizeStringEvent.OnUpdateString.RemoveListener(HandleStringUpdated);
            }
        }

        // 표면 넘실거림이 매 프레임 갱신돼야 하므로 채움 값이 그대로여도 계속 다시 그린다.
        private void LateUpdate()
        {
            ApplyFill();
        }

        /// <summary>진행률(0~1)을 받아 채움 높이로 쓴다.</summary>
        public void SetProgress(float value01)
        {
            fillAmount = Mathf.Clamp01(value01);
        }

        private void ApplyGradient()
        {
            if (fillText == null) return;

            fillText.enableVertexGradient = true;

            // TMP의 정점 그라디언트는 **글자마다** 적용된다(줄 전체가 아니라). 액체 표현에서는
            // 오히려 글자마다 위아래 색이 갈려 자연스럽다.
            fillText.colorGradient = new VertexGradient(fillTop, fillTop, fillBottom, fillBottom);
        }

        private void PickRandomTip()
        {
            if (localizeStringEvent == null)
            {
                Debug.LogWarning("[Loading] LocalizeStringEvent가 연결되지 않아 문구를 고르지 못합니다.", this);

                return;
            }

            if (tipKeys == null || tipKeys.Count == 0)
            {
                Debug.LogWarning("[Loading] 로딩 문구 키 목록이 비어 있습니다.", this);

                return;
            }

            string key = tipKeys[Random.Range(0, tipKeys.Count)];

            // 찬 글자에도 같은 문자열이 들어가야 한다. 영구 리스너가 baseText를 채우므로
            // 여기서는 fillText만 따라가게 붙인다 — 로케일이 바뀌어도 이 경로로 같이 갱신된다.
            localizeStringEvent.OnUpdateString.AddListener(HandleStringUpdated);

            localizeStringEvent.StringReference.SetReference(tableCollection, key);
            localizeStringEvent.RefreshString();
        }

        private void HandleStringUpdated(string value)
        {
            if (fillText != null) fillText.text = value;
        }

        private void ApplyFill()
        {
            if (fillMask == null || baseText == null) return;

            float height = ((RectTransform)baseText.transform).rect.height;

            // 찬 글자의 사각형을 원본과 같게 유지한다. 이게 어긋나면 글자가 위아래로 밀려
            // 두 겹이 어긋난 채 겹친다(문구 길이에 따라 레이아웃 높이가 바뀐다).
            if (fillText != null)
            {
                var fillRect = (RectTransform)fillText.transform;

                if (!Mathf.Approximately(fillRect.sizeDelta.y, height))
                {
                    fillRect.sizeDelta = new Vector2(fillRect.sizeDelta.x, height);
                }
            }

            float filled = height * fillAmount;

            // 다 찼거나 완전히 비었을 때는 넘실거리지 않는다 — 경계에서 삐져나오거나
            // 다 찬 뒤에도 표면이 보이면 "완료"로 안 읽힌다.
            if (surfaceWavePixels > 0f && fillAmount > 0.001f && fillAmount < 0.999f)
            {
                filled += surfaceWavePixels *
                    Mathf.Sin(Time.unscaledTime * surfaceWaveSpeed * Mathf.PI * 2f);
            }

            fillMask.sizeDelta = new Vector2(
                fillMask.sizeDelta.x,
                Mathf.Clamp(filled, 0f, height));
        }
    }
}
