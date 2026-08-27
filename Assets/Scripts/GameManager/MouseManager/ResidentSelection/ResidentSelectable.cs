using UnityEngine;

/// 주민에 붙어 "호버·단일 선택·그룹 선택 참여 가능"을 선언하는 마커. 타워의 `TowerGroupSelectable`과
/// 같은 역할·같은 계보다 — `Resident`(ManagementSpace 소유)를 한 줄도 건드리지 않으려고 별도 컴포넌트로 뺐고,
/// `ResidentSelectionCoordinator`가 런타임에 부착한다(프리팹이 `Assets/Imported`의 별도 저장소에 있어
/// 에셋을 고치지 않는 편이 낫고, 스포너가 만드는 주민에도 자동으로 먹어야 한다).
///
/// MouseManager는 이 마커의 유무만 보고 "주민"이라는 사실은 모른다(입력 단일 창구·제네릭 유지).
/// 도메인 해석과 인원 상한은 소비처인 `ResidentSelectionCoordinator`가 담당한다.
///
/// ⚠️ `MouseManager`는 `hit.collider.TryGetComponent<T>`로 대상을 찾는다 — **GameObject당 인터페이스 구현을
/// 하나만** 잡고 부모 탐색도 하지 않는다. 그래서 `IHoverable`·`ISelectable`·`IGroupSelectable`·`IDragHandle`을
/// 이 컴포넌트 하나에 모아 둔다. 나중에 주민 툴팁을 별도 컴포넌트로 빼면 툴팁이나 아웃라인 중
/// 하나가 조용히 죽는다(`TowerGroupSelectable`에 같은 경고가 있다) → 반드시 여기 합칠 것.
/// 마커는 콜라이더와 같은 GameObject(주민 루트)에 있어야 한다.
[DisallowMultipleComponent]
public class ResidentSelectable : MonoBehaviour, IGroupSelectable, IHoverable, ISelectable, IOutlineKindFilter,
    IDragHandle, ITutorialSelectionGate
{
    private Resident _resident;

    public Resident Resident => _resident != null ? _resident : (_resident = GetComponent<Resident>());

    public TutorialAction SelectionAction => TutorialAction.SelectResident;

    private void Awake() => _resident = GetComponent<Resident>();

    // 드래그 사각형 선택의 후보 목록에 스스로 등록/해제한다. 사각형은 레이캐스트로 판정할 수 없어
    // 후보를 순회해야 하는데, 매 프레임 씬을 긁지 않으려면 대상이 자기 존재를 알려야 한다.
    // 투영 기준점은 이 컴포넌트의 Transform(= 주민 루트, 콜라이더와 같은 GO)이다.
    private void OnEnable() => GroupSelectableRegistry.Register(this, transform);
    private void OnDisable() => GroupSelectableRegistry.Unregister(this);

    // ── 그룹 선택 = 초록 아웃라인 ────────────────────────────────────────
    // 코디네이터의 RefreshHighlight가 diff로 대칭 호출하는 단일 경로다.
    public void OnGroupSelected() => Highlight?.Set(OutlineKind.GroupSelected, true);
    public void OnGroupDeselected() => Highlight?.Set(OutlineKind.GroupSelected, false);

    // ── 호버 = 노란 아웃라인 / 단일 선택 = 초록 아웃라인 ──────────────────
    // 두 색의 실제 구동은 `OutlineInteractionDriver`가 MouseManager 이벤트로 대칭 처리한다.
    // 훅 안에서 켜면 훅 호출이 비대칭인 경로에서 잔존한다(WL-087과 같은 함정) → 여기서는 비워 둔다.
    // 이 구현체가 존재하는 것만으로 "이 주민은 호버·선택 대상"이 성립한다.
    public TooltipContent? GetTooltipContent() => null;
    public void OnHoverEnter() { }
    public void OnHoverExit() { }
    public void OnSelected() { }
    public void OnDeselected() { }

    // ── 들어서 끌 수 있다 (IDragHandle) ─────────────────────────────────
    //
    // 멤버 없는 순수 마커다. 이 컴포넌트가 붙어 있다는 사실만으로 "주민 위에서 좌드래그를 시작하면
    // 사각형이 아니라 유닛 끌기"가 성립한다(MouseManager.md §5.4). 누구를 몇 명 드는지, 놓았을 때
    // 배치되는지는 전부 `ResidentDragCoordinator`가 정한다 — 마커는 판정에 참여하지 않는다.

    // ── 인원 상한을 아웃라인 종류별로 게이팅 ─────────────────────────────
    //
    // 가용 인원(유휴 주민)이 0이면 **선택 초록만** 막는다. 단일 클릭의 초록은 코디네이터가 아니라
    // `OutlineInteractionDriver`가 `MouseManager.OnSelectionChanged`로 직접 켜므로, 코디네이터가
    // 집합을 거부해도 초록은 그대로 뜬다 — 그 경로를 막는 유일한 자리가 여기다.
    //
    // **호버 노랑은 살려 둔다.** 이전에는 상한 0이면 주민을 Selectable 레이어에서 내려 레이캐스트
    // 자체를 끊었는데, 그러면 호버·툴팁까지 사라져 "고를 수 없다"와 "대상이 아니다"가 구분되지 않고
    // 나중에 거절 피드백을 걸 자리도 없어진다(WL-158).
    public bool AllowsOutline(OutlineKind kind)
    {
        if (kind != OutlineKind.Selected)
        {
            return true;
        }

        var coordinator = ResidentSelectionCoordinator.Instance;

        // 코디네이터가 아직 없으면 막지 않는다 — 상한을 모르는 상태에서 거부하면 조용히 안 뜬다.
        return coordinator == null || coordinator.SelectionCap > 0;
    }

    private OutlineHighlight Highlight => OutlineHighlight.GetOrAdd(gameObject);
}
