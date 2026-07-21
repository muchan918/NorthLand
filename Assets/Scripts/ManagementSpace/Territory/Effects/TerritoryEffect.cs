using UnityEngine;

/// <summary>
/// 영토 확보 시 적용되는 <b>단일 효과</b>의 추상 기반(ScriptableObject) — TerritoryGraph.md §5 권장안.<br/>
/// 효과의 <b>데이터(수치)와 행동(Apply)을 함께</b> SO로 주입한다. 새 효과 = 이 클래스를 상속한 SO 하나 추가이며,
/// 그래프/전이/뷰/주입 코드는 건드리지 않는다(§5 "신규 효과가 생겨도 코드 불변").<br/>
/// <br/>
/// 즉시 효과(자원 지급 등)와 패시브 효과(생산량 +X% 등)를 별도 기반 타입으로 나누지 않는다 —
/// 둘 다 <see cref="Apply"/> 한 메서드로 표현하고, 무엇을 건드리는지는 <see cref="TerritoryEffectContext"/>가
/// 노출하는 표면(지갑 지급 / 주민 심 / 미래의 생산 modifier 등)으로 구분한다.<br/>
/// <br/>
/// 수치는 각 서브클래스 SO의 인스펙터 필드에 직접 authoring한다(팀 회의 결정: 영토 효과는 CSV가 아닌 SO 단독 관리).
/// </summary>
public abstract class TerritoryEffect : ScriptableObject
{
    [Tooltip("에디터/디버그용 짧은 설명. 게임 표시 문자열이 아니라 제작 편의용 라벨.")]
    [SerializeField, TextArea] protected string _editorNote;

    /// <summary>
    /// 이 효과를 대상 노드에 1회 적용한다. 확보 직후(OnNodeClaimed) 정확히 한 번 호출될 예정이다(§4.3).<br/>
    /// 즉시 효과는 여기서 <c>ctx.Wallet.Add(...)</c>/<c>ctx.GainResidents(...)</c>로 지급하고,
    /// 패시브 효과는 컨텍스트가 제공할 modifier 심에 등록한다(생산 modifier 훅은 후속 — 아래 서브클래스 주석 참조).
    /// </summary>
    public abstract void Apply(in TerritoryEffectContext ctx);
}
