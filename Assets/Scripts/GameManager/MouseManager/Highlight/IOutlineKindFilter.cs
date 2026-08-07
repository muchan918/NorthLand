/// 아웃라인 **종류별로** 표시를 거부할 수 있는 대상이 구현한다(#302).
///
/// `IOutlineTargetProvider`와 같은 계보다 — 드라이버가 대상에게 물어보고, 대상이 스스로 답한다.
/// 그래서 `OutlineInteractionDriver`는 여전히 도메인을 모른다.
///
/// **왜 `IOutlineTargetProvider`로는 안 되는가**: 그쪽은 `null`을 돌려 아웃라인을 끌 수 있지만
/// `Resolve()`가 호버·선택에 **공용**이라 한쪽만 끌 수 없다. 주민은 "가용 인원이 0이면 선택은
/// 막지만 호버는 살려 둔다"가 필요하다 — 호버까지 끄면 고를 수 없다는 것과 대상이 아니라는 것이
/// 구분되지 않고, 나중에 거절 피드백(흔들림·토스트)을 걸 자리도 사라진다(WL-158).
///
/// 구현체는 `MouseManager`가 잡는 인터페이스(`ISelectable`/`IHoverable`)와 **같은 컴포넌트**에
/// 두어야 한다 — `TryGetComponent`가 GameObject당 하나만 잡기 때문이다.
public interface IOutlineKindFilter
{
    /// 이 종류의 아웃라인을 지금 표시해도 되는가. `false`면 드라이버가 대상 없음으로 취급한다.
    bool AllowsOutline(OutlineKind kind);
}
