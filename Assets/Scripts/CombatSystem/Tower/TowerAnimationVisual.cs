using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NorthLand.Combat
{
    // 타워 모델의 Animator를 타워 생애 사건(발사·설치·해제)에 맞춰 재생하는 얇은 연출 컴포넌트(#359).
    // `TowerReloadVisual`(탄약 모형 숨김)·`TowerTurretAim`(포탑 선회)과 같은 축이다 — 전투 판정은 액션이
    // 하고 이 컴포넌트는 **보이는 것만** 맞춘다. 붙이지 않아도 전투 결과는 한 톨도 달라지지 않는다.
    //
    // 필요한 이유: FattyPoly 터렛 팩(CrossBow·Culverin) 같은 모델 팩의 컨트롤러는 파라미터가 전부
    // Trigger고 기본 상태가 Idle이다 — **누가 켜주지 않으면 모델은 영원히 Idle만 돈다.** 프리팹을 아무리
    // 제대로 물려도 모션이 안 나오는 이유가 여기 있고, 예외도 경고도 없다.
    //
    // ## 발사는 Trigger, 설치·해제는 상태 직접 재생인 이유
    // 컨트롤러 그래프가 그렇게 생겼다. 발사는 `Idle → Fire`(cond Fire) → exitTime으로 `Idle` 복귀라
    // 트리거가 정확히 맞는 도구다. 반면 **`Idle → Install` 전이가 아예 없다** — Install은 `Remove`
    // 상태에서만 도달할 수 있게 저작돼 있어서, 배치 직후(=Idle) `SetTrigger("Install")`은 아무 일도
    // 하지 않는다. 설치·해제는 되돌아올 필요 없는 일회성 상태 전환이므로 `Play`로 직접 넣는다.
    // (벤더 트리의 컨트롤러를 고치는 것은 CLAUDE.md 금지 항목이라 코드 쪽에서 받는다.)
    //
    // ## 발사를 Trigger로 켤 때와 상태로 직접 재생할 때
    // Trigger는 **전이가 일어날 때만** 소비된다. 그래서 발사 간격이 발사 클립보다 짧아지면(공격 속도 버프,
    // 연사형 타워) 다음 발사의 트리거가 Fire 상태 안에서 소비되지 못하고 대기하다가, 클립이 끝난 뒤에야
    // 다시 재생된다 — **사격은 빨라지는데 반동은 클립 주기로 고정돼 매 발 어긋난다.**
    // `fireState`를 채우면 `Play(..., 0f)`로 매번 처음부터 재생해 그 어긋남이 구조적으로 사라진다.
    // FattyPoly 팩처럼 발사 클립의 실제 움직임이 앞부분에 몰려 있고(CrossBow_Fire는 0.85초 중 앞 0.25초)
    // 뒤가 빈 구간이면, 중간에 잘려도 잃는 그림이 없으므로 이쪽이 낫다.
    //
    // ## 장전 모션의 `reloadDelay`를 고르는 법
    // **발사 클립 길이가 아니라 그 타워의 공격 간격에서 역산한다.** 발사가 끝난 뒤에 장전을 붙이면
    // 한 발의 연출 길이가 `발사 + 장전`이 되는데, 그게 공격 간격보다 길면 연출이 매 발마다 뒤처져
    // 나중에는 사격과 눈에 띄게 어긋난다(아처: 0.85 + 0.617 = 1.47초 > 간격 1초).
    // 그래서 `Fire → Reload` 전이가 `exitTime=False`로 저작돼 있다 — **발사 도중 끼어들라는 뜻**이다.
    //   reloadDelay ≒ 공격 간격 − 장전 클립 길이   (아처: 1.0 − 0.617 ≒ 0.4)
    // 간격이 장전 클립보다 짧은 타워는 0으로 두면 된다(기본값). 공격 속도 버프로 간격이 줄어드는 경우는
    // `HandleFired`의 재예약이 알아서 장전을 건너뛴다.
    //
    // ## 설치 모션이 곧바로 시작하지 않는 이유
    // 배치 순간 등장 연출(#264 `TowerSpawnEffect`)이 **타워 루트의 스케일을 0으로 붙잡는다.** 그 창은
    // `ConvergeDuration`(입자 수렴) 동안 이어지고, 그 뒤에야 팝으로 원본 크기가 된다. `OnEnable`에서
    // 곧바로 재생하면 1초짜리 설치 클립의 **절반 가까이가 안 보이는 동안 지나간다** — 예외도 경고도 없이
    // "설치 모션이 짧게 잘린 것처럼" 보이는 게 전부다.
    // 그래서 기본 지연을 `TowerSpawnEffect.ConvergeDuration`으로 둔다. 그 상수가 `public`인 이유가
    // 정확히 이것이다 — #265의 합성 입자도 같은 시간 축을 공유하려고 공개돼 있다.
    // 등장 연출을 타지 않는 경로(씬 선배치·테스트 씬)는 그만큼 늦게 시작할 뿐이라 무해하다.
    //
    // ★ 두 가지가 이 지연의 함정이고 둘 다 닫아 뒀다(WL-179).
    //  ① **시간축** — 대기를 `Invoke`(스케일드)로 재면 배속에서 어긋난다. 안 보이는 구간은 unscaled라
    //     실시간 0.45초로 고정인데 대기만 짧아지고, Animator가 `updateMode = Normal`이라 클립까지
    //     빨라진다. 실측: x2에서 약 73%, x4에서는 **100%**가 안 보이는 동안 지나갔다. 일시정지 중
    //     배치하면 타워는 (unscaled라) 다 서는데 대기만 멈춰 있다가, 해제 후 뜬금없이 모션이 났다.
    //     → `UniTask.Delay(..., DelayType.UnscaledDeltaTime)`로 연출과 같은 축에서 기다린다.
    //  ② **기본값을 상수로 적으면 단일 출처가 아니다** — 직렬화 필드의 초기화자는 컴포넌트를 처음 붙일
    //     때만 적용되고 그 뒤로는 프리팹 YAML에 숫자로 굳는다. 수렴 시간을 나중에 튜닝하면 코드 참조는
    //     전부 따라가는데 프리팹만 옛 값에 남고, `ConvergeDuration`을 grep해도 안 걸린다.
    //     → **음수를 "연출을 따른다"는 센티넬로** 쓰고 해석을 `OnEnable`로 옮겼다.
    //
    // ## 해제는 왜 자동으로 걸리지 않나
    // **현재 타워 철거 경로 자체가 없다**(`Docs/Core/Tower.md` §6 #1). 유일한 파괴 경로인 합성 소모는
    // `TowerDissolveEffect`가 시각 사본을 뜬 뒤 **같은 프레임에** 원본을 `Destroy`하므로, 원본에서
    // 모션을 재생해도 재생될 시간이 없다. 그래서 `PlayRemove()`를 공개 메서드로 열어두고 호출자를
    // 기다린다 — 철거 UI가 생기면 파괴 전에 부르면 된다.
    [RequireComponent(typeof(Tower))]
    public class TowerAnimationVisual : MonoBehaviour
    {
        [Tooltip("모션을 가진 Animator. 미할당이면 Awake에서 자식에서 찾는다 — " +
                 "모델 프리팹이 타워 루트의 자식으로 들어가는 현재 프리팹 구조를 그대로 받는다.")]
        [SerializeField] Animator animator;

        [Tooltip("발사 시 켤 Trigger 파라미터 이름. FattyPoly 터렛 팩은 CrossBow·Culverin 모두 \"Fire\"다. " +
                 "비우면 발사 모션을 재생하지 않는다.")]
        [SerializeField] string fireTrigger = "Fire";

        [Tooltip("발사 상태를 이름으로 직접 재생한다(예: \"CrossBow_Fire\"). 채우면 fireTrigger 대신 쓰인다 — " +
                 "발사마다 처음부터 다시 재생되므로 공격 속도가 빨라져도 반동이 사격과 어긋나지 않는다. " +
                 "이유는 클래스 주석 참고.")]
        [SerializeField] string fireState;

        [Tooltip("발사 후 이어서 켤 장전 Trigger 이름. 비우면 장전 모션을 재생하지 않는다.")]
        [SerializeField] string reloadTrigger = "Reload";

        [Tooltip("발사 후 장전 모션까지의 지연(초). **0이면 장전 모션 없음**(기존 타워는 전부 무변경). " +
                 "값 고르는 법은 클래스 주석 참고 — 발사 클립 길이가 아니라 공격 간격에서 역산한다.")]
        [SerializeField] float reloadDelay;

        [Tooltip("사거리에서 적이 사라진 순간 장전 모션을 1회 재생한다(마무리 연출). " +
                 "같은 오브젝트의 TowerTurretAim이 내는 신호를 쓰므로 그 컴포넌트가 있어야 동작한다.")]
        [SerializeField] bool playReloadOnTargetLost;

        [Tooltip("설치 시 직접 재생할 상태 이름. 모델 팩이 \"Install\"이라 이름 붙인 상태가 반드시 정답은 " +
                 "아니다 — 아처(CrossBow)는 Install이 '하늘에서 낙하'라 등장 연출과 부딪혀서 \"CrossBow_Reload\"를 " +
                 "쓴다. Trigger가 아니라 상태 이름인 이유는 클래스 주석 참고. 비우면 재생하지 않는다.")]
        [SerializeField] string installState;

        [Tooltip("해제 시 직접 재생할 상태 이름(예: \"CrossBow_Remove\"). 비우면 해제 모션을 재생하지 않는다.")]
        [SerializeField] string removeState;

        [Tooltip("활성화될 때 설치 모션을 자동 재생한다. 배치 직후 1회가 여기서 나온다 — " +
                 "씬에 미리 놓인 타워도 씬 시작 시 한 번 설치 모션을 탄다.")]
        [SerializeField] bool playInstallOnEnable = true;

        [Tooltip("활성화 후 설치 모션까지의 지연(초). **음수 = 등장 연출의 수렴 시간을 따른다**(기본). " +
                 "숫자를 적어 굳혀두면 그 연출을 튜닝할 때 프리팹만 옛 값에 남는다 — 이유는 클래스 주석 참고. " +
                 "0이면 즉시 재생한다.")]
        [SerializeField] float installDelay = -1f;

        Tower _tower;
        TowerTurretAim _turretAim;
        int _fireHash;
        int _fireStateHash;
        int _reloadHash;
        int _installHash;
        int _removeHash;
        // 설치 지연 대기를 활성화 주기에 묶는다. `destroyCancellationToken`이 아니라 자체 CTS인 이유는
        // 파괴뿐 아니라 **비활성화**에서도 끊겨야 하기 때문이다(기존 `CancelInvoke`가 하던 역할).
        CancellationTokenSource _installCts;

        void Awake()
        {
            _tower = GetComponent<Tower>();
            _turretAim = GetComponent<TowerTurretAim>();

            if (playReloadOnTargetLost && _turretAim == null)
                Debug.LogWarning($"[TowerAnimationVisual] {name}: TowerTurretAim이 없어 " +
                                 "'적 소실 시 장전' 연출이 나오지 않습니다.", this);

            // 인스펙터 미할당 폴백. `true`는 비활성 자식도 훑는다 — 연출용으로 꺼둔 노드 아래에
            // Animator가 들어가도 배선이 조용히 끊기지 않게 한다.
            if (animator == null) animator = GetComponentInChildren<Animator>(true);

            // 문자열 대신 해시로 들고 있는다(매 발사 문자열 해싱 회피). 이름이 비면 0이라 아래에서 건너뛴다.
            _fireHash = Hash(fireTrigger);
            _fireStateHash = Hash(fireState);
            _reloadHash = Hash(reloadTrigger);
            _installHash = Hash(installState);
            _removeHash = Hash(removeState);

            // 이 컴포넌트가 하는 일이 재생 요청뿐이라, 배선이 없으면 **무증상으로 아무 모션도 안 난다**.
            // 그 침묵이 정확히 이 컴포넌트가 존재하는 이유이므로 여기서는 소리를 낸다.
            if (animator == null || animator.runtimeAnimatorController == null)
                Debug.LogWarning($"[TowerAnimationVisual] {name}: Animator 또는 컨트롤러가 없어 모션이 나오지 않습니다.", this);
        }

        static int Hash(string name) => string.IsNullOrEmpty(name) ? 0 : Animator.StringToHash(name);

        void OnEnable()
        {
            _tower.OnFired += HandleFired;
            if (playReloadOnTargetLost && _turretAim != null) _turretAim.TargetLost += PlayReload;

            if (!playInstallOnEnable) return;

            // 음수 = "등장 연출을 따른다". 값을 굳히지 않고 여기서 해석하므로 `ConvergeDuration`이
            // 진짜 단일 출처로 남는다 — 그 연출을 튜닝하면 이미 저장된 프리팹도 함께 따라온다.
            float delay = installDelay < 0f ? TowerSpawnEffect.ConvergeDuration : installDelay;

            if (delay <= 0f)
            {
                PlayInstall();
                return;
            }

            _installCts = new CancellationTokenSource();
            DelayedInstallAsync(delay, _installCts.Token).Forget();
        }

        void OnDisable()
        {
            _tower.OnFired -= HandleFired;
            if (playReloadOnTargetLost && _turretAim != null) _turretAim.TargetLost -= PlayReload;

            // 수렴 중에 꺼진 타워가 되살아나며 두 번 설치되지 않게
            _installCts?.Cancel();
            _installCts?.Dispose();
            _installCts = null;

            CancelInvoke(nameof(PlayReload));
        }

        /// 설치 모션을 지연 후 재생한다. **반드시 unscaled로 기다린다** — 이 지연이 겨냥하는
        /// `TowerSpawnEffect`의 수렴과 팝이 `Time.unscaledDeltaTime` 축이기 때문이다(SystemMap §6).
        ///
        /// 스케일드로 재면 배속에서 축이 갈린다: 안 보이는 구간은 실시간 0.45초로 고정인데 대기는
        /// 그만큼 짧아지고, 게다가 Animator가 `updateMode = Normal`이라 클립까지 빨라진다 —
        /// 실측으로 x2에서 약 73%, x4에서는 **100%가 안 보이는 동안 지나갔다**(WL-179).
        async UniTaskVoid DelayedInstallAsync(float delay, CancellationToken ct)
        {
            bool canceled = await UniTask
                .Delay(TimeSpan.FromSeconds(delay), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct)
                .SuppressCancellationThrow();

            if (!canceled) PlayInstall();
        }

        void HandleFired()
        {
            PlayFire();

            if (_reloadHash == 0 || reloadDelay <= 0f) return;

            // 직전 발사가 예약해 둔 장전을 취소하고 다시 잡는다. 공격 속도 버프로 간격이 `reloadDelay`보다
            // 짧아지면 장전 예약이 매번 밀려나 **아예 재생되지 않는다** — 연사 중에 장전 모션이 사격보다
            // 뒤처져 쌓이는 것을 이 한 줄이 구조적으로 막는다(트리거를 미리 켜두는 방식으로는 못 막는다).
            //
            // ⚠ 위 설치 지연과 **의도적으로 다른 시간축이다.** 이 값은 공격 간격에서 역산한 것이고 공격
            //    쿨다운이 `Time.deltaTime`(스케일드)으로 도니, 배속에서 사격과 함께 줄어야 맞다. Animator도
            //    스케일드라 셋이 같은 축에 있다. 그래서 여기는 `Invoke`(스케일드)가 정답이고, 설치는
            //    unscaled 연출을 겨냥하므로 아니다 — 두 지연이 같은 API를 쓰지 않는 것이 요점이다(WL-179).
            CancelInvoke(nameof(PlayReload));
            Invoke(nameof(PlayReload), reloadDelay);
        }

        /// 발사 모션. `fireState`가 채워져 있으면 그 상태를 **매번 처음부터** 재생하고, 비어 있으면
        /// `fireTrigger`를 켠다(그래프의 전이 규칙에 맡기는 기존 경로).
        public void PlayFire()
        {
            if (!Ready()) return;

            if (_fireStateHash != 0)
            {
                PlayState(_fireStateHash, fireState);
                return;
            }

            if (_fireHash != 0) animator.SetTrigger(_fireHash);
        }

        /// 장전 모션. 발사 후 `reloadDelay`만큼 뒤에 자동으로 불린다.
        public void PlayReload()
        {
            if (_reloadHash == 0 || !Ready()) return;

            animator.SetTrigger(_reloadHash);
        }

        /// 설치 모션. 배치 직후 `OnEnable`에서 자동으로 불린다.
        public void PlayInstall() => PlayState(_installHash, installState);

        /// 해제 모션. **자동 호출자가 없다** — 철거 경로가 생기면 파괴 전에 부를 것(클래스 주석 참고).
        public void PlayRemove() => PlayState(_removeHash, removeState);

        void PlayState(int stateHash, string stateName)
        {
            if (stateHash == 0 || !Ready()) return;

            // 저작 실수(상태 이름 오타·모델 교체로 사라진 상태)를 드러낸다. `Play`는 없는 상태를 받으면
            // 조용히 아무것도 하지 않으므로, 이 확인이 없으면 "왜 모션이 안 나오지"가 다시 무증상이 된다.
            if (!animator.HasState(0, stateHash))
            {
                Debug.LogWarning($"[TowerAnimationVisual] {name}: 컨트롤러에 \"{stateName}\" 상태가 없습니다.", this);
                return;
            }

            // 정규화 시간 0 = 항상 처음부터. 전이를 거치지 않으므로 그래프에 진입 경로가 없어도 재생된다.
            animator.Play(stateHash, 0, 0f);
        }

        bool Ready() => animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;
    }
}
