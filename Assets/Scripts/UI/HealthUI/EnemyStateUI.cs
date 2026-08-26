using System;
using NorthLand.Combat;
using TMPro;
using UnityEngine;

namespace NorthLand.UI
{
    /// 몬스터 한 마리의 머리 위 상태 표시 위젯 — **체력바(#447) + 상태이상 아이콘(#502)**.
    ///
    /// ⚠ **몬스터 프리팹의 자식이 아니다.** <see cref="EnemyStateUILayer"/>가 소유한 공용 월드
    /// 캔버스 아래에 살면서 대상 몬스터를 따라다닌다. 프리팹에 심지 않는 이유가 이 이슈의 출발점이다 —
    /// 자식으로 두면 (a) 몬스터 루트 스케일이 바 크기를 끌고 가서 프리팹마다 손튜닝 스케일이 붙고
    /// (실측 편차 3.3배), (b) 신규 몬스터 프리팹에서 누락돼도 아무 신호가 없으며(WL-055),
    /// (c) 마리마다 캔버스가 하나씩 생겨 드로우콜이 마릿수만큼 늘어난다.
    ///
    /// 폭·높이는 이 프리팹의 RectTransform `sizeDelta`가 단일 출처이고, 레이어 캔버스의 고정 스케일이
    /// 곱해져 **모든 몬스터가 같은 월드 폭**을 갖는다.
    ///
    /// **아이콘은 런타임에 만들지 않는다**(#502). 종류별로 프리팹에 미리 배치해 두고 `SetActive`로만
    /// 켠다 — 종류가 넷으로 고정이고 한 마리가 동시에 갖는 수도 그 이하라, `Instantiate`를 돌리면
    /// 밤 웨이브에서 **몬스터 수 × 상태 변화 횟수**만큼 할당이 튄다.
    [RequireComponent(typeof(RectTransform))]
    public class EnemyStateUI : MonoBehaviour
    {
        /// 상태이상 한 종류와 그것을 그리는 오브젝트의 짝. 프리팹에서 저작한다.
        [Serializable]
        struct StatusIcon
        {
            public EffectKind kind;
            public GameObject root;

            [Tooltip("중첩 수를 적을 라벨(선택). 중첩되지 않는 종류(스턴)는 비워 둔다.")]
            public TMP_Text count;
        }

        [Tooltip("체력 비율만큼 가로로 차는 이미지. anchorMax.x로 채운다.")]
        [SerializeField] RectTransform fill;

        [Tooltip("HP 절대량 눈금. 채워진 구간과 빈 구간 모두에 그려야 남은 절대량을 읽을 수 있다.")]
        [SerializeField] HealthBarTicks ticks;

        [Tooltip("몬스터 렌더러 상단에서 바를 얼마나 더 띄울지(월드 단위).")]
        [SerializeField] float topMargin = 1.2f;

        [Header("상태이상 아이콘 (#502)")]
        [Tooltip("종류별 아이콘 오브젝트. 프리팹에 미리 배치해 두고 SetActive로만 켠다. " +
                 "부모에 HorizontalLayoutGroup을 두면 꺼진 아이콘이 레이아웃에서 빠져 남은 것들이 다시 모인다.")]
        [SerializeField] StatusIcon[] statusIcons;

        [Tooltip("처형 표식(#318) 아이콘. 상태이상 4종과 달리 StatusEffectHandler가 아니라 " +
                 "Enemy가 직접 드는 값이라 슬롯을 따로 둔다.")]
        [SerializeField] GameObject executeIcon;

        [Tooltip("1겹일 때 숫자를 감춘다. 끄면 1도 그대로 적는다.")]
        [SerializeField] bool hideCountWhenSingle;

        /// 레이어가 부착 높이를 계산할 때 쓰는 여유값. 저작 위치가 이 프리팹 하나로 모인다.
        public float TopMargin => topMargin;

        Enemy enemy;
        float anchorHeight;

        // 상태이상 조회 대상. **Bind 시점에 없을 수 있다** — StatusEffectHandler는 첫 피격 때
        // AddComponent로 붙기 때문이다(그쪽 클래스 주석). 그래서 캐시가 아니라 지연 해석이다.
        StatusEffectHandler status;
        float statusProbeTimer;

        // 마지막으로 아이콘에 반영한 마스크. 바뀐 프레임에만 SetActive를 친다.
        int appliedIconMask = k_MaskUnset;

        // 마지막으로 라벨에 적은 겹 수(statusIcons와 같은 길이). TMP는 SetText마다 메시를 다시 만들므로
        // **값이 바뀐 프레임에만** 쓴다. -1은 "아직 안 적음".
        int[] appliedCounts;

        // 0은 "아무것도 안 걸림"이라는 유효한 마스크라 초기값으로 쓸 수 없다 — 그러면 첫 프레임에
        // 아이콘을 끄는 동작이 생략되어 프리팹에 켜진 채로 저장된 아이콘이 그대로 남는다.
        const int k_MaskUnset = -1;

        // 마스크 비트 0~3은 EffectKind(화상·독·감속·스턴)가 쓴다. 처형은 EffectKind가 아니므로
        // 그 위 비트를 따로 쓴다 — EffectKind에 값을 더하지 않는 이유는 그 enum이 타워 SO 저작
        // (SerializeReference)과 effectId 해시에 함께 쓰여, 표시용 값을 섞으면 저작 드롭다운에
        // "처형"이 뜨고 해시 공간도 함께 흔들리기 때문이다.
        const int k_ExecuteBit = 1 << 4;

        // 핸들러가 아직 없을 때 존재를 다시 확인하는 주기(초). 붙기 전에는 걸린 상태이상도 없으므로
        // 늦게 알아채도 표시가 틀리지 않는다 — 매 프레임 GetComponent를 때리지 않기 위한 값이다.
        const float k_StatusProbeInterval = 0.25f;

        /// 대상 몬스터에 붙인다. <paramref name="worldAnchorHeight"/>는 몬스터 루트 기준 월드 Y 오프셋이다
        /// (렌더러 상단 + <see cref="TopMargin"/>, 레이어가 스폰 시 1회 계산).
        public void Bind(Enemy target, float worldAnchorHeight)
        {
            Release();

            enemy = target;
            anchorHeight = worldAnchorHeight;

            // 풀에서 꺼낸 위젯은 직전 몬스터의 핸들러와 마스크를 들고 있다. 여기서 끊지 않으면
            // 새 몬스터의 아이콘이 남의 상태를 그린다.
            status = null;
            statusProbeTimer = 0f;
            ResetAppliedCounts();
            ApplyIconMask(0);

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
            RefreshStatusIcons();
            return true;
        }

        /// 걸린 상태이상 집합을 읽어 아이콘을 켜고 끈다(#502).
        ///
        /// 이벤트 구독이 아니라 **폴링**인 이유: 이 위젯은 레이어가 매 LateUpdate마다 <see cref="Follow"/>를
        /// 부르므로 읽을 자리가 이미 있고, 위젯이 풀링되는 구조라 구독/해제 수명을 하나 더 만들면
        /// 회수 시 해제를 빠뜨렸을 때 죽은 위젯이 계속 갱신되는 종류의 버그가 생긴다.
        void RefreshStatusIcons()
        {
            int mask = 0;

            // 처형 표식은 Enemy가 직접 든다 — 핸들러가 아직 안 붙은 몬스터에게도 걸릴 수 있으므로
            // 핸들러 조회와 **독립적으로** 먼저 본다.
            if (enemy != null && enemy.HasExecuteMark)
            {
                mask |= k_ExecuteBit;
            }

            if (status == null)
            {
                statusProbeTimer -= Time.deltaTime;

                if (statusProbeTimer <= 0f)
                {
                    statusProbeTimer = k_StatusProbeInterval;

                    if (enemy != null)
                    {
                        enemy.TryGetComponent(out status);
                    }
                }
            }

            if (status != null)
            {
                mask |= status.ActiveKindMask;
            }

            ApplyIconMask(mask);

            // ⚠ 겹 수는 **마스크가 그대로여도 바뀐다**(2겹 → 3겹). 그래서 마스크 변경 검사 밖에서 본다.
            RefreshStackCounts(mask);
        }

        /// 겹 수 라벨 갱신(#502). 라벨이 없는 아이콘(중첩되지 않는 종류)은 건너뛴다.
        void RefreshStackCounts(int mask)
        {
            if (statusIcons == null || statusIcons.Length == 0)
            {
                return;
            }

            if (appliedCounts == null || appliedCounts.Length != statusIcons.Length)
            {
                appliedCounts = new int[statusIcons.Length];
                ResetAppliedCounts();
            }

            for (int i = 0; i < statusIcons.Length; i++)
            {
                TMP_Text label = statusIcons[i].count;

                if (label == null)
                {
                    continue;
                }

                int count = 0;

                if (status != null && (mask & StatusEffectHandler.MaskOf(statusIcons[i].kind)) != 0)
                {
                    count = status.CountOf(statusIcons[i].kind);
                }

                if (count == appliedCounts[i])
                {
                    continue;
                }

                appliedCounts[i] = count;

                bool show = count > 0 && (!hideCountWhenSingle || count > 1);

                if (label.gameObject.activeSelf != show)
                {
                    label.gameObject.SetActive(show);
                }

                if (show)
                {
                    // SetText(format, arg)는 int를 문자열로 만들지 않는 무할당 경로다.
                    label.SetText("{0}", count);
                }
            }
        }

        void ResetAppliedCounts()
        {
            if (appliedCounts == null)
            {
                return;
            }

            for (int i = 0; i < appliedCounts.Length; i++)
            {
                appliedCounts[i] = -1;
            }
        }

        void ApplyIconMask(int mask)
        {
            if (mask == appliedIconMask)
            {
                return;
            }

            appliedIconMask = mask;

            if (executeIcon != null)
            {
                executeIcon.SetActive((mask & k_ExecuteBit) != 0);
            }

            if (statusIcons == null)
            {
                return;
            }

            for (int i = 0; i < statusIcons.Length; i++)
            {
                GameObject root = statusIcons[i].root;

                if (root == null)
                {
                    continue;
                }

                root.SetActive((mask & StatusEffectHandler.MaskOf(statusIcons[i].kind)) != 0);
            }
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

            status = null;
            statusProbeTimer = 0f;
            ResetAppliedCounts();
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
