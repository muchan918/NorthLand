/// <summary>
/// 호버했을 때 <b>어떤 커서를 보여줄지 스스로 답하는</b> 대상이 구현한다.
///
/// <see cref="IOutlineKindFilter"/>와 정확히 같은 계보다 — 드라이버가 대상에게 물어보고, 대상이 답한다.
/// 그래서 <see cref="CursorController"/>는 "건물이면 문, 타워면 돋보기"라는 도메인 지식을 갖지 않는다
/// (<c>MouseManager.md</c> §1 원칙 2). 새 타입에 커서를 붙이는 비용은 프로퍼티 한 줄이고,
/// 컨트롤러는 수정되지 않는다.
///
/// 구현하지 않은 호버 대상은 <see cref="CursorKind.Default"/>로 취급된다 — 즉 이 인터페이스는
/// <b>선택 사항</b>이고, 안 붙였다고 호버가 깨지지 않는다.
///
/// ⚠ <see cref="IHoverable"/>과 <b>같은 GameObject</b>(콜라이더가 붙은 루트)에 두어야 한다.
/// <c>MouseManager</c>가 <c>hit.collider.TryGetComponent</c>로 대상을 잡고 부모를 타지 않기 때문이다.
/// 되도록 <see cref="IHoverable"/>을 구현한 <b>그 컴포넌트에</b> 합쳐 둘 것 — 별도 컴포넌트로 빼면
/// GameObject당 하나만 잡히는 규칙에 걸려 조용히 죽는 조합이 생긴다
/// (<c>TowerGroupSelectable</c>·<c>ResidentSelectable</c>에 같은 경고가 있다).
/// </summary>
public interface ICursorHint
{
    /// <summary>이 대상 위에 커서가 올라갔을 때 보여줄 커서. 매 호버 전환마다 1회 읽는다.</summary>
    CursorKind HoverCursor { get; }
}
