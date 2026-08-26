using NorthLand.Combat;
using UnityEngine;

namespace NorthLand.UI
{
    /// 몬스터 한 마리의 머리 위 체력바 위젯(#447).
    ///
    /// ⚠ **몬스터 프리팹의 자식이 아니다.** <see cref="MonsterHealthBarLayer"/>가 소유한 공용 월드
    /// 캔버스 아래에 살면서 대상 몬스터를 따라다닌다. 프리팹에 심지 않는 이유가 이 이슈의 출발점이다 —
    /// 자식으로 두면 (a) 몬스터 루트 스케일이 바 크기를 끌고 가서 프리팹마다 손튜닝 스케일이 붙고
    /// (실측 편차 3.3배), (b) 신규 몬스터 프리팹에서 누락돼도 아무 신호가 없으며(WL-055),
    /// (c) 마리마다 캔버스가 하나씩 생겨 드로우콜이 마릿수만큼 늘어난다.
    ///
    /// 폭·높이는 이 프리팹의 RectTransform `sizeDelta`가 단일 출처이고, 레이어 캔버스의 고정 스케일이
    /// 곱해져 **모든 몬스터가 같은 월드 폭**을 갖는다.
    [RequireComponent(typeof(RectTransform))]
    public class MonsterHealthBar : MonoBehaviour
    {
        [Tooltip("체력 비율만큼 가로로 차는 이미지. anchorMax.x로 채운다.")]
        [SerializeField] RectTransform fill;

        [Tooltip("HP 절대량 눈금. 채워진 구간과 빈 구간 모두에 그려야 남은 절대량을 읽을 수 있다.")]
        [SerializeField] HealthBarTicks ticks;

        [Tooltip("몬스터 렌더러 상단에서 바를 얼마나 더 띄울지(월드 단위).")]
        [SerializeField] float topMargin = 1.2f;

        /// 레이어가 부착 높이를 계산할 때 쓰는 여유값. 저작 위치가 이 프리팹 하나로 모인다.
        public float TopMargin => topMargin;

        Enemy enemy;
        float anchorHeight;

        /// 대상 몬스터에 붙인다. <paramref name="worldAnchorHeight"/>는 몬스터 루트 기준 월드 Y 오프셋이다
        /// (렌더러 상단 + <see cref="TopMargin"/>, 레이어가 스폰 시 1회 계산).
        public void Bind(Enemy target, float worldAnchorHeight)
        {
            Release();

            enemy = target;
            anchorHeight = worldAnchorHeight;

            if (enemy == null)
            {
                return;
            }

            enemy.OnHpChanged += UpdateBar;

            // Enemy.Spawned가 Start에서 오므로 이 MaxHp는 이미 웨이브 배율이 반영된 확정값이다
            // (Enemy.Spawned 선언부 ①). OnHpChanged 구독은 이후의 피해·회복을 받기 위한 것이다.
            UpdateBar(enemy.CurrentHp, enemy.MaxHp);
        }

        /// 대상 위치로 따라간다. **false면 대상이 사라졌거나 죽은 것**이라 레이어가 회수한다.
        public bool Follow(Camera cam)
        {
            // 사망 연출(destroyDelay) 동안 바가 시체 위에 남지 않도록 IsDead에서 바로 뗀다.
            if (enemy == null || enemy.IsDead)
            {
                return false;
            }

            transform.position = SnapToPixelGrid(enemy.transform.position + Vector3.up * anchorHeight, cam);
            return true;
        }

        /// 바 위치를 **화면 픽셀 격자에 스냅**한다.
        ///
        /// 왜 필요한가: 몬스터가 움직이면 바도 매 프레임 서브픽셀만큼 밀린다. 이 프로젝트는
        /// MSAA와 포스트 AA가 **둘 다 꺼져 있어서**(`PC_RPAsset` MSAA=1 · 카메라 `antialiasing=None`)
        /// 그 서브픽셀 이동이 얇은 눈금선·바 테두리를 프레임마다 다른 픽셀에 얹고, 그게 자글거림으로 보인다.
        /// 위치를 정수 픽셀에 고정하면 몬스터가 움직여도 바 내부의 샘플링 위치가 프레임 간 그대로 유지된다.
        ///
        /// 대가는 바가 1픽셀 단위로 끊겨 움직인다는 것인데, 바는 몬스터 머리 위 표식이지 몬스터 자체가
        /// 아니라서 눈에 띄지 않는다. 줌 중에는 배율이 매 프레임 바뀌므로 이 스냅이 무력하다(의도).
        static Vector3 SnapToPixelGrid(Vector3 world, Camera cam)
        {
            if (cam == null)
            {
                return world;
            }

            Vector3 screen = cam.WorldToScreenPoint(world);

            // 카메라 뒤로 넘어가면 변환이 뒤집힌다 — 스냅을 포기하고 원래 좌표를 쓴다.
            if (screen.z <= 0f)
            {
                return world;
            }

            screen.x = Mathf.Round(screen.x);
            screen.y = Mathf.Round(screen.y);

            return cam.ScreenToWorldPoint(screen);
        }

        public void Release()
        {
            if (enemy != null)
            {
                enemy.OnHpChanged -= UpdateBar;
                enemy = null;
            }
        }

        void OnDestroy()
        {
            Release();
        }

        void UpdateBar(float current, float max)
        {
            // MaxHp==0은 EnemyAsset 스탯 미설정(WL-001) 상태 — 바 전체를 숨긴다.
            // 오브젝트를 꺼도 이 핸들러는 계속 불리므로(구독은 Enemy 쪽에 산다) 값이 채워지면 되살아난다.
            bool hasHp = max > 0f;

            if (gameObject.activeSelf != hasHp)
            {
                gameObject.SetActive(hasHp);
            }

            if (!hasHp)
            {
                return;
            }

            if (fill != null)
            {
                Vector2 anchorMax = fill.anchorMax;
                anchorMax.x = Mathf.Clamp01(current / max);
                fill.anchorMax = anchorMax;
            }

            if (ticks != null)
            {
                ticks.SetMaxHp(max);
            }
        }
    }
}
