using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    /// <summary>
    /// 빔 연출. <see cref="BeamAction"/>이 매 프레임 대상별 (원점·착점·램프 진행도)를 먹이고,
    /// 이 컴포넌트가 진행도에 맞는 **단계**의 굵기·색으로 빔을 그린다.
    ///
    /// <para><b>왜 액션이 아니라 프리팹 컴포넌트인가</b><br/>
    /// 액션 설계 규칙 ①이 "액션은 수치를 갖지 않는다"다(실제로 액션 중 `[SerializeField]`를 쓰는 것이
    /// 하나도 없다). 굵기·색 같은 연출 수치를 `BeamAction`에 두면 그 규칙이 깨지므로, `TowerAnimationVisual`
    /// ·`TowerReloadVisual`과 같은 "얇은 연출 컴포넌트" 자리로 뺀다. `BeamAction`의 기존 주석도
    /// "빔 비주얼 — 최소 구현… 연출 폴리싱은 별도 이슈"로 이 분리를 예고해 두었다.</para>
    ///
    /// <para><b>왜 연속 보간이 아니라 단계인가</b><br/>
    /// 램프가 오르고 있다는 것을 플레이어가 **알아채야** 하는 연출이다. 굵기를 연속으로 키우면
    /// 변화가 프레임당 미세해 "세지고 있다"가 읽히지 않는다. 단계로 끊으면 전환 순간이 사건이 되어
    /// 눈에 걸린다(COC 인페르노가 같은 이유로 단계형이다).</para>
    ///
    /// <para><b>⚠ 최고 단계 문턱을 1.0에 붙이지 말 것</b><br/>
    /// 진행도는 `달성 스택 ÷ MaxStacks`다. `single_inferno`는 만렙까지 3.36초가 걸리는데 적 체류가
    /// 3.33초라 **8스택에 도달하지 못한다**(CombatBalance.md §3.2가 이미 진단). 문턱을 1.0 근처에 두면
    /// 그 단계가 화면에 영원히 안 나온다 — 기본값을 0.7로 둔 이유다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class TowerBeamVisual : MonoBehaviour
    {
        /// 램프 진행도 한 구간의 겉모습.
        [Serializable]
        public class Stage
        {
            [Tooltip("에디터에서 알아보기 위한 이름. 런타임에는 쓰지 않는다.")]
            public string Label;

            [Tooltip("이 단계가 시작되는 진행도(0~1). 진행도 이하 중 **가장 높은** 문턱의 단계가 쓰인다. " +
                     "첫 단계는 0이어야 한다.")]
            [Range(0f, 1f)] public float FromProgress;

            [Tooltip("빔 굵기(월드 단위).")]
            public float Width = 0.5f;

            [Tooltip("빔 색. 머티리얼이 정점 색을 쓰므로 알파도 반영된다.")]
            [ColorUsage(true, true)] public Color Color = Color.white;
        }

        [Tooltip("램프 진행도에 따른 단계. 낮은 문턱부터 순서대로 저작할 것.")]
        [SerializeField]
        Stage[] stages =
        {
            new Stage { Label = "1단계 (조준 시작)", FromProgress = 0.00f, Width = 0.30f, Color = new Color(0.45f, 0.75f, 1f, 0.85f) },
            new Stage { Label = "2단계 (가열)",      FromProgress = 0.35f, Width = 0.55f, Color = new Color(1f, 0.85f, 0.35f, 0.95f) },
            new Stage { Label = "3단계 (최대)",      FromProgress = 0.70f, Width = 0.95f, Color = new Color(1f, 0.35f, 0.15f, 1f) },
        };

        [Tooltip("빔 머티리얼. 비우면 BeamAction의 최소 구현과 같은 Sprites/Default 머티리얼을 만들어 쓴다.")]
        [SerializeField] Material beamMaterial;

        readonly List<LineRenderer> _beams = new();
        Material _runtimeMaterial;

        /// <summary>
        /// 대상 하나의 빔을 그린다. <paramref name="progress"/>는 램프 진행도(0~1)이며,
        /// 램프가 저작되지 않은 타워(멀티 인페르노)는 0이 들어와 항상 1단계로 그려진다.
        /// </summary>
        public void Draw(int index, Vector3 origin, Vector3 target, float progress)
        {
            LineRenderer lr = Rent(index);
            if (lr == null) return;

            Stage stage = StageFor(progress);
            if (stage != null)
            {
                lr.widthMultiplier = stage.Width;
                // startColor/endColor로 넣는다 — 머티리얼을 단계마다 복제하지 않으려는 것이고,
                // 그래야 단계 전환이 인스턴스 생성 없이 같은 프레임에 반영된다.
                lr.startColor = stage.Color;
                lr.endColor = stage.Color;
            }

            lr.enabled = true;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, target);
        }

        /// <summary>
        /// 빔 하나를 끈다. `HideFrom`이 꼬리만 끄는 것과 달리 **중간 인덱스**를 끌 수 있다 —
        /// 잠금 목록에 남아 있지만 그릴 수 없는 대상(사망 연출 중 등)이 유령 빔으로 남지 않게 한다.
        /// </summary>
        public void Hide(int index)
        {
            if (index < 0 || index >= _beams.Count) return;
            if (_beams[index] != null) _beams[index].enabled = false;
        }

        /// <summary><paramref name="keep"/>개만 남기고 나머지 빔을 끈다.</summary>
        public void HideFrom(int keep)
        {
            for (int i = keep < 0 ? 0 : keep; i < _beams.Count; i++)
            {
                if (_beams[i] != null) _beams[i].enabled = false;
            }
        }

        public void HideAll() => HideFrom(0);

        // 꺼질 때 빔이 켜진 채 굳지 않게 한다 — BeamAction의 OnWaveEnd가 없을 때 빔이 낮까지
        // 남았던 것과 같은 종류의 사고를 컴포넌트 쪽에서도 한 번 더 막는다.
        void OnDisable() => HideAll();

        void OnDestroy()
        {
            if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
        }

        /// 진행도가 속한 단계. 문턱 이하 중 가장 높은 것을 고른다 — 저작 순서가 뒤섞여도 결과가 같다.
        Stage StageFor(float progress)
        {
            if (stages == null || stages.Length == 0) return null;

            Stage best = null;
            for (int i = 0; i < stages.Length; i++)
            {
                Stage s = stages[i];
                if (s == null || s.FromProgress > progress) continue;
                if (best == null || s.FromProgress >= best.FromProgress) best = s;
            }

            // 첫 단계 문턱이 0보다 크게 저작된 경우(진행도 0에서 후보가 없다) 가장 낮은 단계로 떨어진다.
            if (best != null) return best;

            Stage lowest = null;
            for (int i = 0; i < stages.Length; i++)
            {
                if (stages[i] == null) continue;
                if (lowest == null || stages[i].FromProgress < lowest.FromProgress) lowest = stages[i];
            }
            return lowest;
        }

        LineRenderer Rent(int index)
        {
            if (index < 0) return null;

            while (_beams.Count <= index) _beams.Add(Create());
            return _beams[index];
        }

        LineRenderer Create()
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.material = Material();
            lr.enabled = false;
            return lr;
        }

        Material Material()
        {
            if (beamMaterial != null) return beamMaterial;
            if (_runtimeMaterial == null) _runtimeMaterial = new Material(Shader.Find("Sprites/Default"));
            return _runtimeMaterial;
        }
    }
}
