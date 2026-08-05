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
/// 하나만** 잡고 부모 탐색도 하지 않는다. 그래서 `IHoverable`·`ISelectable`·`IGroupSelectable`을
/// 이 컴포넌트 하나에 모아 둔다. 나중에 주민 툴팁을 별도 컴포넌트로 빼면 툴팁이나 아웃라인 중
/// 하나가 조용히 죽는다(`TowerGroupSelectable`에 같은 경고가 있다) → 반드시 여기 합칠 것.
/// 마커는 콜라이더와 같은 GameObject(주민 루트)에 있어야 한다.
[DisallowMultipleComponent]
public class ResidentSelectable : MonoBehaviour, IGroupSelectable, IHoverable, ISelectable
{
    private Resident _resident;

    public Resident Resident => _resident != null ? _resident : (_resident = GetComponent<Resident>());

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

    private OutlineHighlight Highlight => OutlineHighlight.GetOrAdd(gameObject);
}
