using UnityEngine;
using UnityEngine.UI;

namespace NorthLand.UI
{
    /// 체력바 위에 **HP 절대량 눈금**을 그리는 UI 그래픽(#447).
    /// 얇은 줄 = <see cref="minorUnit"/>(기본 100 HP), 굵은 줄 = <see cref="majorUnit"/>(기본 1000 HP).
    ///
    /// 바 폭은 전 몬스터 공통 고정이고 **칸 수만** MaxHp에 따라 늘어난다(LoL 방식). 그래서
    /// "남은 칸 수 = CurrentHp ÷ 단위"가 몬스터 종류와 무관하게 성립하고, 절대량 기준으로 조준하는
    /// 「체력 높은 적」 정책(`TargetingPolicy`)과 화면이 어긋나지 않는다.
    ///
    /// ⚠ **텍스처 반복(RawImage + Repeat)으로 그리지 않는다.** 반복 UV는 칸이 늘수록 한 칸의 화면 폭이
    /// 줄고 그 안의 선 두께가 **같이** 줄어서, 칸이 많은 개체(Tank 2600 = 얇은 줄 24개)에서 선이
    /// 서브픽셀로 내려가 뭉치거나 깜빡인다. 메시로 그리면 선 두께가 칸 수와 무관하게 고정이다.
    /// 스프라이트가 없어 <see cref="Graphic.mainTexture"/>가 흰 텍스처라 같은 캔버스의 배경·필과
    /// **한 배치로 묶인다**(눈금 때문에 늘어나는 드로우콜 0).
    [AddComponentMenu("UI/NorthLand/Health Bar Ticks")]
    public class HealthBarTicks : MaskableGraphic
    {
        // 눈금 하나가 뜻하는 HP. 이 두 값이 "칸당 HP"의 단일 출처다.
        [SerializeField] float minorUnit = 100f;
        [SerializeField] float majorUnit = 1000f;

        // 캔버스 px 기준 선 두께. 바 자체가 캔버스 px로 저작되므로 칸 수와 무관하게 일정하다.
        [SerializeField] float minorWidth = 10f;
        [SerializeField] float majorWidth = 20f;

        [SerializeField] Color minorColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] Color majorColor = new Color(0f, 0f, 0f, 0.95f);

        [Tooltip("얇은 줄의 높이 비율. 1이면 굵은 줄과 같은 전체 높이.")]
        [Range(0.1f, 1f)]
        [SerializeField] float minorHeightRatio = 1f;

        // 폭주 방지 상한. HP가 예상 밖으로 커져도(밸런스 실험값 등) 정점 수가 터지지 않게 자른다.
        // 자른 사실은 화면에서 "눈금이 오른쪽 끝까지 안 그려짐"으로 드러난다.
        const int k_MaxTicks = 256;

        float maxHp;

        /// 눈금이 표현할 최대 HP. **웨이브 배율이 곱해진 실효 MaxHp**를 넣는다.
        public void SetMaxHp(float value)
        {
            if (Mathf.Approximately(maxHp, value))
            {
                return;
            }

            maxHp = value;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (maxHp <= 0f || minorUnit <= 0f)
            {
                return;
            }

            Rect rect = GetPixelAdjustedRect();

            // 굵은 줄 주기(얇은 줄 몇 개마다). 100/1000이면 10.
            int majorEvery = majorUnit > minorUnit
                ? Mathf.Max(1, Mathf.RoundToInt(majorUnit / minorUnit))
                : 0;

            int count = Mathf.Min(Mathf.CeilToInt(maxHp / minorUnit) - 1, k_MaxTicks);

            for (int k = 1; k <= count; k++)
            {
                float hp = k * minorUnit;

                // 바 오른쪽 끝(=MaxHp)에는 줄을 긋지 않는다 — 테두리와 겹쳐 칸 하나를 더 세게 만든다.
                if (hp >= maxHp)
                {
                    break;
                }

                bool major = majorEvery > 0 && k % majorEvery == 0;
                float width = major ? majorWidth : minorWidth;
                float height = major ? rect.height : rect.height * minorHeightRatio;
                float half = width * 0.5f;

                // 끝단에서 선이 바 밖으로 삐져나오지 않도록 가둔다.
                float x = Mathf.Clamp(
                    rect.x + rect.width * (hp / maxHp),
                    rect.x + half,
                    rect.xMax - half);

                float centerY = rect.center.y;

                AddQuad(vh,
                    x - half, centerY - height * 0.5f,
                    x + half, centerY + height * 0.5f,
                    major ? majorColor : minorColor);
            }
        }

        static void AddQuad(VertexHelper vh, float xMin, float yMin, float xMax, float yMax, Color32 color)
        {
            int i = vh.currentVertCount;

            vh.AddVert(new Vector3(xMin, yMin), color, Vector2.zero);
            vh.AddVert(new Vector3(xMin, yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(xMax, yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(xMax, yMin), color, Vector2.zero);

            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

#if UNITY_EDITOR
        // 인스펙터에서 단위·두께를 만지면 즉시 다시 그린다(저작 중 눈으로 확인하는 값들이다).
        protected override void OnValidate()
        {
            base.OnValidate();
            SetVerticesDirty();
        }
#endif
    }
}
