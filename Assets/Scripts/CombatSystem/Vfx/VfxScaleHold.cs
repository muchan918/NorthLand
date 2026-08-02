using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NorthLand.Combat
{
    /// 연출이 대상 루트의 `localScale`을 **배타적으로 점유**하는 창을 관리한다.
    /// 등장 연출(#264 `TowerSpawnEffect`)과 합성 재조립(#265)이 둘 다 "0에서 원본으로 뽕 튀어나오기"를
    /// 하므로, 그 점유·복원·인계를 이 한 곳으로 모았다.
    ///
    /// ## 왜 안전장치가 필요한가
    /// 같은 대상에 두 연출이 겹치면 **두 번째가 이미 0이 된 스케일을 원본으로 캡처**해 대상이 영구히
    /// 보이지 않게 된다. "안 보이는 타워"는 이 계열 연출의 최악 실패 모드고, 배치·합성·재조립이 서로를
    /// 모른 채 같은 타워에 재생을 걸 수 있는 이상 우연에 맡길 수 없다.
    ///
    /// ## 왜 static 딕셔너리가 아니라 **대상에 붙는 컴포넌트**인가
    /// 예전 구현은 `Dictionary&lt;Transform, Effect&gt;` 레지스트리였는데, 연출 도중 대상이 사라지면
    /// (합성 소모·철거) 파괴된 Transform이 키로 남아 누수·오매칭을 걱정해야 했다 — Unity의 `==`가
    /// 파괴된 객체를 서로 같다고 판정하는 탓에 참조 비교자까지 따로 필요했다.
    /// 컴포넌트로 두면 **점유 상태가 대상과 함께 죽으므로** 그 문제가 통째로 사라진다. 조회도
    /// `TryGetComponent` 한 번이다. 런타임 컴포넌트 부착은 `TowerFootprint`/`TowerGroupSelectable`로
    /// 이미 확립된 패턴이다.
    ///
    /// ## 사용법
    /// <code>
    /// _hold = VfxScaleHold.Acquire(target); // 이전 점유자가 있으면 그 자리에서 원복시키고 인계
    /// _hold.SetFactor(0f);                  // 숨김
    /// await _hold.PopAsync(0.28f, ct);      // back-out으로 원본까지 → 끝나면 자동 Release
    /// // 어떤 경로로 끊겨도 OnDestroy에서 _hold.Release()를 부를 것(원복 최후 방어선)
    /// </code>
    [DisallowMultipleComponent]
    public sealed class VfxScaleHold : MonoBehaviour
    {
        private const float k_PopOvershoot = 1.70158f; // back-out 이징 표준 계수

        private Vector3 _originalScale;
        private bool _held;
        // 점유 세대. `Acquire`마다 증가해 **이전에 발급한 핸들을 전부 무효화**한다 — 인계된 연출이
        // 남은 프레임에서 스케일을 한 번 더 덮는 것을 이 값 하나로 막는다.
        private int _generation;

        /// 발급받은 점유권. 인계·대상 파괴 후에는 모든 호출이 조용히 무시된다.
        public readonly struct Handle
        {
            private readonly VfxScaleHold _hold;
            private readonly int _generation;

            internal Handle(VfxScaleHold hold, int generation)
            {
                _hold = hold;
                _generation = generation;
            }

            /// 아직 내가 점유 중인가. 대상 파괴(가짜 null)·인계·이미 Release됨을 한 번에 판정한다.
            public bool IsValid => _hold != null && _hold._held && _hold._generation == _generation;

            /// 같은 대상에 **새 연출이 들어와 자리를 뺏겼는가.** `IsValid`와 달리 정상 종료(Release)와
            /// 구분된다 — 연출이 "내 차례가 끝났으니 남은 구간을 접자"를 판단하는 신호로 쓴다.
            /// 스케일을 건드리지 않는 구간(입자·링)까지 접어야 인계 후 두 연출의 잔상이 겹치지 않는다.
            public bool IsSuperseded => _hold != null && _hold._generation != _generation;

            /// 원본 스케일에 곱할 배율(0 = 숨김, 1 = 원본).
            public void SetFactor(float factor)
            {
                if (IsValid) _hold.transform.localScale = _hold._originalScale * factor;
            }

            /// 원본 스케일로 확정하고 점유를 놓는다. 두 번 불러도 무해하다.
            public void Release()
            {
                if (IsValid) _hold.ReleaseInternal();
            }

            /// 0 → back-out 오버슈트 → 원본. "뽕!"의 정체는 이 오버슈트다. 끝나면 스스로 Release한다.
            public UniTask PopAsync(float duration, CancellationToken ct)
                => _hold == null ? UniTask.CompletedTask : _hold.RunPopAsync(this, duration, ct);
        }

        /// 대상의 스케일 점유를 시작한다. 이미 점유 중이면 **그 자리에서 원본을 되돌리고** 인계받는다 —
        /// `Destroy`만으로는 늦다(파괴는 프레임 끝이라 그 사이 새 연출이 0을 원본으로 캡처한다).
        public static Handle Acquire(Transform target)
        {
            if (target == null) return default;

            if (!target.TryGetComponent(out VfxScaleHold hold))
            {
                hold = target.gameObject.AddComponent<VfxScaleHold>();
            }

            hold.ReleaseInternal();                 // 이전 점유자의 원본을 지금 복원(점유 중이 아니면 무해)
            hold._originalScale = target.localScale; // `Vector3.one`이 아니라 원본 — 루트 스케일이 1이라는 보장이 없다
            hold._held = true;
            hold._generation++;
            return new Handle(hold, hold._generation);
        }

        private async UniTask RunPopAsync(Handle handle, float duration, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime; // 배속·일시정지 무관 — 아래 OnDestroy 주석 참고
                if (!handle.IsValid) return;       // 인계·대상 파괴 시 남은 구간을 조용히 접는다

                handle.SetFactor(BackOut(Mathf.Clamp01(elapsed / duration)));
                await UniTask.Yield(ct);
            }

            handle.Release(); // 누적 오차 없이 원본 스케일로 확정
        }

        private void ReleaseInternal()
        {
            if (!_held) return;
            _held = false;
            transform.localScale = _originalScale;
        }

        // 대상이 파괴되면 같이 사라진다 — 여기서 원복할 대상도 함께 없어지므로 남길 상태가 없다.
        // (연출 쪽이 끊겼을 때의 원복은 연출 호스트의 OnDestroy가 `Handle.Release()`로 책임진다.)
        private void OnDestroy() => _held = false;

        private static float BackOut(float t)
        {
            const float c3 = k_PopOvershoot + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + k_PopOvershoot * u * u;
        }
    }
}
