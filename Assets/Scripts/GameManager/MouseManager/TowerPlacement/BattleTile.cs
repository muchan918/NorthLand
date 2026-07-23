using UnityEngine;

/// 전투 맵 타일의 종류. grass/road/lava 프리팹에 대응.
public enum TileKind
{
    Grass, // 건설 가능
    Road,  // 몬스터 경로 — 배치 불가
    Lava,  // 위험 타일 — 배치 불가
}

/// 전투 맵 타일의 종류·점유 상태를 담는 데이터 마커.
/// grass/road/lava 타일 프리팹에 부착되며, 타워 배치 검증(TowerPlacer)이 이 값을 읽는다.
/// 맵빌더 로직은 이 컴포넌트를 몰라도 되며(부착만), 여기엔 판정 로직을 두지 않는다.
public class BattleTile : MonoBehaviour
{
    [SerializeField] TileKind _kind = TileKind.Grass;
    [SerializeField] Transform _anchor = null;

    public TileKind Kind => _kind;

    /// 타워가 앉을 기준점(타일 윗면에 둔 자식 앵커). 배치 시 레이의 hit.point.y(옆면에 맞으면 벽면 높이)
    /// 대신 이 위치의 y를 써서 타워가 항상 타일 윗면에 앉게 한다(TowerPlacer). _anchor 미지정 시
    /// 타일 자신의 트랜스폼으로 폴백해 무설정 타일도 안전하다.
    public Vector3 AnchorPosition => _anchor != null ? _anchor.position : transform.position;

    /// 타워가 이 타일을 점유 중인지(런타임 상태). 타일 생성 시 false, 배치 확정 시 true.
    /// 타일이 파괴·재생성(맵 리셋)되면 자연히 초기화된다.
    public bool Occupied { get; set; }
}
