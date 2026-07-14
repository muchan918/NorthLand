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
    [SerializeField] private TileKind kind = TileKind.Grass;

    public TileKind Kind => kind;

    /// 타워가 이 타일을 점유 중인지(런타임 상태). 타일 생성 시 false, 배치 확정 시 true.
    /// 타일이 파괴·재생성(맵 리셋)되면 자연히 초기화된다.
    public bool Occupied { get; set; }
}
