using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 영토 노드 하나에 주입되는 <b>효과 묶음</b>(ScriptableObject) — TerritoryGraph.md §5.<br/>
/// 하나 이상의 <see cref="TerritoryEffect"/>를 참조하며, 확보 시 이 정의가 가진 효과들이 순서대로 1회 적용될 예정이다.<br/>
/// 노드 생성 시점에 정의 풀에서 <b>중복 없이</b> 각 노드에 배정된다(<see cref="TerritoryController"/> 참조).<br/>
/// <br/>
/// 표시명/설명은 로컬라이제이션 스트링 테이블 키로 둔다(#102 계보). 표시 문자열 출처 자체는 §5 note에서 열어둔
/// 사항이며, 여기서는 효과 <b>행동</b>과 무관한 authored 키로만 보유한다(효과 수치는 각 효과 SO가 직접 authoring).
/// </summary>
[CreateAssetMenu(fileName = "TerritoryDefinition", menuName = "Scriptable Objects/Territory/Territory Definition")]
public class TerritoryDefinition : ScriptableObject
{
    private const string k_StringTableName = "NorthLand_Territories";
    // 스트링 테이블 키 접두사 — 실제 authored 키(예: territories.m10.name)에 맞춰 복수형 사용.
    private const string k_DisplayNameKeyPrefix = "territories";

    [Tooltip("영토 이름 표시용 스트링 테이블 키(로컬라이제이션). 비워도 주입/적용에는 지장 없음.")]
    [SerializeField] string _id;

    [Tooltip("확보 시 적용할 효과들. 즉시(자원/주민)·패시브(생산 배율) 등 혼합 가능.")]
    [SerializeField] List<TerritoryEffect> _effects = new();

    public string DisplayNameKey => $"{k_DisplayNameKeyPrefix}.{_id}.name";
    public string DescriptionKey => $"{k_DisplayNameKeyPrefix}.{_id}.desc";
    public IReadOnlyList<TerritoryEffect> Effects => _effects;

    /// <summary>
    /// 이 정의의 모든 효과를 대상 노드에 순서대로 적용한다(확보 직후 1회 호출 예정 — 배선은 WL-030 후속).<br/>
    /// null 효과 슬롯은 건너뛴다.
    /// </summary>
    public void ApplyAll(in TerritoryEffectContext ctx)
    {
        if (_effects == null)
        {
            return;
        }

        for (int i = 0; i < _effects.Count; i++)
        {
            _effects[i]?.Apply(ctx);
        }
    }
}
