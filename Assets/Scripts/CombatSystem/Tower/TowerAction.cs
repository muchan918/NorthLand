using System;
using UnityEngine;

namespace NorthLand.Combat
{
    // 이 액션이 도는 페이즈. 게이팅은 각 액션이 아니라 호스트(Tower)가 이 값을 보고 대신 처리한다 —
    // 액션마다 DayNightManager를 직접 폴링하면 페이즈 규칙이 조용히 갈라진다(WL-044가 정확히 그 사고였다:
    // Tower는 밤 게이팅이 있는데 AuraTower의 버프 경로엔 없어서 낮에도 상시 동작).
    public enum TowerActivePhase
    {
        // 밤에만 동작. 공격·디버프 오라 — 전투 행위.
        NightOnly,

        // 페이즈 무관. 버프 오라 — 배치 즉시(낮 포함) 효과가 보여야 낮 정보 패널의 스탯이 맞는다(#164).
        Always,
    }

    // 타워가 하는 일 한 조각. 타워가 "무엇을 하는 물건인지"는 전부 이 구현체들이 가지며,
    // Tower는 정체성(SO/진영)·스탯 원장·선택 표현·레지스트리만 소유한다.
    //
    // **MonoBehaviour가 아니다.** 프리팹의 `Tower.Actions`에 `[SerializeReference]`로 직렬화되어
    // 인스펙터에서 조립된다. 그래서 프리팹만 보면 이 타워가 무엇을 하는지 알 수 있고,
    // 새 타워를 추가할 때 코드를 건드릴 일이 없다(#274).
    //
    // 설계 규칙 4가지 — 어기면 원래 문제로 되돌아간다. 근거: Docs/Core/TowerRedesign.md §4
    //  ① **수치를 갖지 않는다.** 전부 TowerAsset에서 읽는다. 여기 두면 밸런싱 값이 프리팹으로
    //     내려가 머지 충돌이 늘고 데이터 테이블화 경로(WL-015)에서 멀어진다.
    //  ② **배선은 Tower가 갖는다.** firePoint·enemyLayerMask는 Owner를 통해 읽는다.
    //  ③ **런타임 상태는 직렬화하지 않는다.** [SerializeReference] 객체는 Instantiate 시 인스턴스마다
    //     깊은 복사되므로, 직렬화만 안 하면 런타임 상태는 자동으로 타워별 독립이 된다.
    //  ④ **소스 ID는 SourceId를 쓴다.** 아래 주석 참조.
    [Serializable]
    public abstract class TowerAction
    {
        // 호스트. Initialize가 채우며 직렬화되지 않는다(규칙 ③).
        [NonSerialized] protected Tower Owner;

        // 액션은 컴포넌트가 아니므로 자기 Transform이 없다 — 위치는 호스트 것을 쓴다.
        protected Transform Origin => Owner.transform;

        /// 스탯 원장·상태이상 핸들러에 넘길 소스 키(규칙 ④).
        ///
        /// 액션엔 `GetInstanceID()`가 없으므로 **호스트 인스턴스 ID + 액션 타입**으로 채번한다.
        /// `Owner`가 타워마다 다르므로 "같은 종류 오라 타워 여러 기가 서로 합산 중첩"이 그대로 보존된다.
        /// 이 성질이 깨졌던 전례가 있다 — 예전에 디버프 오라가 TowerID 해시를 써서 감속 타워를 2기 지어도
        /// 배율이 1중첩에 머물렀고, 보스 P1 돌진의 유일한 파훼 수단이 무력화됐다.
        ///
        /// ⚠ 한 타워에 같은 타입 액션이 둘 있으면 키가 충돌한다 — TowerAsset.OnValidate가 막는다.
        protected int SourceId => Owner.GetInstanceID() ^ GetType().Name.GetHashCode();

        public abstract TowerActivePhase ActivePhase { get; }

        /// 호스트가 조립 시 호출한다. `Owner` 대입을 잊을 수 없도록 진입점은 비가상으로 두고
        /// 실제 초기화는 `OnInitialize`가 받는다.
        public void Initialize(Tower owner, TowerAsset asset)
        {
            Owner = owner;
            OnInitialize(asset);
        }

        protected abstract void OnInitialize(TowerAsset asset);

        /// 호스트가 페이즈 게이팅 후 구동한다. 액션이 스스로 Update를 돌지 않는다.
        public abstract void Tick(float deltaTime);

        /// 호스트 비활성화(철거·풀 반환) 시 호출된다 — 외부에 남긴 상태를 여기서 걷어낸다.
        public abstract void Dispose();

        /// 웨이브가 끝났을 때(밤→낮 전환) 호출된다. **"이 웨이브 동안만" 유효한 상태를 버리는 자리**다
        /// — 성장 스택(#300)이 그 첫 소비처다. 기본 구현은 아무것도 하지 않으므로 대부분의 액션은
        /// 이 훅을 몰라도 된다.
        ///
        /// ⚠ **누가 부르는가: 호스트다.** `Tick`의 페이즈 게이팅과 같은 이유로 액션이 직접
        /// `DayNightManager`를 구독하지 않는다 — 액션마다 구독하면 규칙이 갈라지고(WL-044가 정확히 그
        /// 사고였다), 구독 해제를 잊은 액션이 파괴된 타워를 계속 건드린다. 호스트가 이미 매 프레임
        /// 페이즈를 읽고 있으므로 그 값의 전이를 보는 것이 신호원을 하나로 유지하는 길이다.
        ///
        /// ⚠ `ActivePhase`와 **무관하게** 호출된다. 밤에만 도는 액션도 웨이브 종료는 들어야 한다.
        ///
        /// 스킬 쪽도 같은 모양의 훅을 요구받고 있다(#202 `SkillEffect.OnWaveEnd`) — 두 축이 같은
        /// 규약을 쓰도록 이름과 브로드캐스트 방식을 맞췄다.
        public virtual void OnWaveEnd() { }

        /// 선택 시 바닥에 그릴 사거리 원의 반경. 이 액션이 "닿는 거리" 개념을 갖지 않으면 0.
        ///
        /// 호스트가 `AttackRange`로 대신 그릴 수 없는 이유: 그 값은 공격 액션에서만 나오므로
        /// 오라 전용 타워에서 0이 되어 원이 사라진다(#192 회귀). 표시 반경은 액션마다 근거가
        /// 다르므로(공격=사거리, 오라=오라 반경) `DescribeStats`와 같은 "액션이 자기 표시를 안다" 규약을 따른다.
        public abstract float DisplayRange { get; }

        /// 정보 패널에 이 액션이 기여할 설명 줄. 없으면 null.
        /// 호스트는 조각을 모아 붙이기만 하고 "무엇을 보여줄지"는 각 액션이 안다 —
        /// 예전에는 Tower와 AuraTower가 각자 자기 버전의 스탯 텍스트를 조립했다(WL-079).
        public abstract string DescribeStats();

#if UNITY_EDITOR
        /// 씬 뷰 기즈모. 액션이 MonoBehaviour가 아니게 되면서 OnDrawGizmosSelected를 잃었으므로
        /// 호스트(Tower)가 대신 불러준다.
        public virtual void DrawGizmos() { }
#endif
    }
}
