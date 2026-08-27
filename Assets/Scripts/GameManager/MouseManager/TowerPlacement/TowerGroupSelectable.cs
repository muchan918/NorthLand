using UnityEngine;
using NorthLand.Combat;

/// 배치된 타워에 붙어 "그룹 선택(합성 재료) 참여 가능"을 선언하는 마커(IGroupSelectable 구현).
/// Tower.cs(Combat 소유)를 건드리지 않으려고 별도 컴포넌트로 분리했다. TowerPlacer가 타워 배치 시
/// 런타임으로 부착하므로(AddComponent), 게임 흐름으로 배치된 모든 타워(합성 결과 포함)가 자동으로
/// 그룹 선택 대상이 된다. MouseManager는 이 마커 유무만 보고 대상 타입(타워)은 모른다(제네릭 유지).
///
/// IHoverable도 함께 구현한다(#213 §5.1): `Tower`는 IAttacker·ISelectable만 구현해서
/// `MouseManager.UpdateHover`의 `hit.collider.TryGetComponent<IHoverable>`에 걸리지 않고, 그러면
/// 호버 이벤트 자체가 발생하지 않아 타워에 호버 아웃라인이 뜨지 않는다. 툴팁은 필요 없으니
/// `GetTooltipContent()`는 null을 반환한다(계약상 허용 — "색 변경만 하는 호버 대상", 이슈 #67).
///
/// ⚠️ `TryGetComponent`는 GameObject당 IHoverable **하나만** 잡고 부모 탐색도 하지 않는다.
/// 나중에 타워 툴팁을 추가할 때 별도 컴포넌트를 만들면 툴팁이나 아웃라인 중 하나가 조용히 죽는다
/// → 반드시 이 컴포넌트에 합칠 것. 마커는 콜라이더와 같은 GO(타워 루트)에 유지해야 한다.
[DisallowMultipleComponent]
public class TowerGroupSelectable : MonoBehaviour, IGroupSelectable, IHoverable, ICursorHint
{
    private Tower _tower;

    public Tower Tower => _tower != null ? _tower : (_tower = GetComponent<Tower>());

    private void Awake() => _tower = GetComponent<Tower>();

    // 드래그 사각형 선택(#261)의 후보 목록에 스스로 등록/해제한다. 사각형은 레이캐스트로 판정할 수 없어
    // 후보를 순회해야 하는데, 매 프레임 씬을 긁지 않으려면 대상이 자기 존재를 알려야 한다.
    // 투영 기준점은 이 컴포넌트의 Transform(= 타워 루트, 콜라이더와 같은 GO)이다.
    private void OnEnable() => GroupSelectableRegistry.Register(this, transform);
    private void OnDisable() => GroupSelectableRegistry.Unregister(this);

    // ── 그룹 선택(합성 재료) = 초록 아웃라인 ────────────────────────────
    // 코디네이터의 RefreshHighlight가 diff로 대칭 호출하는 단일 경로다(#213 §5.2).
    // 임시 하늘색 바닥 쿼드는 아웃라인으로 대체됐다(TowerMerge.md §8.4 확정).
    public void OnGroupSelected() => Highlight?.Set(OutlineKind.GroupSelected, true);
    public void OnGroupDeselected() => Highlight?.Set(OutlineKind.GroupSelected, false);

    // ── 호버 = 노란 아웃라인 ────────────────────────────────────────────
    // 실제 색 구동은 OutlineInteractionDriver가 MouseManager.OnHoverChanged로 대칭 처리한다
    // (훅 안에서 켜면 훅 호출이 비대칭인 경로에서 잔존한다 — WL-087과 같은 함정).
    // 이 구현체가 존재하는 것만으로 "이 타워는 호버 대상"이 성립한다.
    public TooltipContent? GetTooltipContent() => null;
    public void OnHoverEnter() { }
    public void OnHoverExit() { }

    // 타워 위에서의 커서(현재 돋보기). 위 ⚠ 경고와 같은 이유로 이 컴포넌트에 함께 둔다 —
    // 별도 컴포넌트로 빼면 GameObject당 하나만 잡히는 규칙에 걸려 조용히 죽는다.
    public CursorKind HoverCursor => CursorKind.Tower;

    private OutlineHighlight Highlight => OutlineHighlight.GetOrAdd(gameObject);
}
