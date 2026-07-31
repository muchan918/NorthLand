using UnityEngine;

/// <summary>
/// 영토 노드 하나에 주입되는 <b>미개척 영지 정의</b>(ScriptableObject) — #166 재설계.<br/>
/// 종전의 "확보 시 즉시 효과(자원/주민/생산배율)" 모델을 폐기하고, 각 영지는 <b>고유한 새 자원 종류 하나</b>를
/// 매일 정산마다 자동 수급하는 원천이 된다(GDD §3.2·§5.3, TerritoryGraph.md §5).<br/>
/// <br/>
/// - <see cref="Kind"/>: 이 영지가 수급하는 자원(금/루비/사파이어/다이아 등).<br/>
/// - <see cref="IslandPrefab"/>: 확보 시 이 노드에 세워지는 섬 프리팹. <b>SO=고정 프리팹</b>(#166) — 노드 Id 기반
///   랜덤 선택(종전 <c>TerritoryNodeStateVisual._mountainPrefabs</c>)에서 SO 소유로 이관.<br/>
/// - <see cref="MinDaily"/>/<see cref="MaxDaily"/>: 매일 수급량 범위. <b>주입 시점에 [Min,Max]에서 1회 롤</b>해
///   해당 노드의 일일량으로 확정된다(<see cref="RollDailyYield"/> — 노드별로 다르지만 이후 고정).<br/>
/// <br/>
/// 표시명/설명은 로컬라이제이션 스트링 테이블(<c>NorthLand_Territories</c>) 키로 둔다(#102 계보).
/// 수치(자원량 범위)는 이 SO의 인스펙터에 직접 authoring한다(영토 데이터는 CSV 예외 — 팀 회의 결정, §5).
/// </summary>
[CreateAssetMenu(fileName = "TerritoryDefinition", menuName = "Scriptable Objects/Territory/Territory Definition")]
public class TerritoryDefinition : ScriptableObject
{
    // 스트링 테이블 키 접두사 — 실제 authored 키(예: territories.gold.name)에 맞춰 복수형 사용.
    private const string k_DisplayNameKeyPrefix = "territories";

    [Tooltip("영지 식별자 — 표시명/설명 스트링 테이블 키 파생용(territories.{id}.name/.desc).")]
    [SerializeField] string _id;

    [Tooltip("이 영지가 매일 수급하는 자원 종류(금/루비/사파이어/다이아 등).")]
    [SerializeField] ResourceKind _kind = ResourceKind.Gold;

    [Tooltip("확보 시 이 노드에 세워지는 섬 프리팹. SO=고정 프리팹(#166) — Owned 비주얼로 스폰된다.")]
    [SerializeField] GameObject _islandPrefab;

    [Tooltip("매일 수급량 최소값(0 이상). 주입 시점에 [Min,Max]에서 1회 롤해 노드에 확정.")]
    [SerializeField, Min(0)] int _minDaily = 1;

    [Tooltip("매일 수급량 최대값(Min 이상 권장 — 역전 시 자동 보정).")]
    [SerializeField, Min(0)] int _maxDaily = 3;

    public ResourceKind Kind => _kind;
    public GameObject IslandPrefab => _islandPrefab;
    public int MinDaily => _minDaily;
    public int MaxDaily => _maxDaily;

    public string DisplayNameKey => $"{k_DisplayNameKeyPrefix}.{_id}.name";
    public string DescriptionKey => $"{k_DisplayNameKeyPrefix}.{_id}.desc";

    /// <summary>
    /// 이 영지의 일일 수급량을 [<see cref="MinDaily"/>, <see cref="MaxDaily"/>] 구간에서 1회 롤한다(양끝 포함).<br/>
    /// 주입(그래프 생성) 시점에 <see cref="TerritoryController"/>가 시드 rng로 호출해 노드별로 확정한다 —
    /// 같은 seed면 같은 값이 재현된다(WL-008 결정성). rng가 null이면 최소값을 반환한다.
    /// </summary>
    public int RollDailyYield(System.Random rng)
    {
        int lo = Mathf.Min(_minDaily, _maxDaily);
        int hi = Mathf.Max(_minDaily, _maxDaily);
        return rng != null ? rng.Next(lo, hi + 1) : lo;
    }
}
