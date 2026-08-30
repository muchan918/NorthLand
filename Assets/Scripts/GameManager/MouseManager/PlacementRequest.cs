using System;
using UnityEngine;

/// "무엇을 / 어디에 놓을 수 있는지 / 놓았을 때 무엇을 할지"를 캡슐화한 배치 요청. (요구사항 ①)
/// 타워·건물 등 배치물 종류가 늘어도 이 요청만 다르게 만들면 된다 → 확장성.
/// 배치 표면 히트(RaycastHit)를 그대로 넘겨, 그리드 스냅·검증·확정을 요청 측이 결정한다.
public class PlacementRequest
{
    public GameObject GhostPrefab; // 마우스를 따라다닐 프리뷰
    public Func<RaycastHit, Vector3> Snap; // 히트 → 배치 기준 위치(그리드 스냅). null이면 hit.point 사용
    // 고스트·배치물의 회전. 그리드가 회전된 맵(`CombatMapTileSpawner.CoordinateRoot`) 아래에 있으면
    // 월드 정렬로 놓으면 타일과 각이 어긋난다 → 요청 측이 그리드 기준축을 넣는다.
    // 배치 세션 동안 상수라 매 프레임 갱신하지 않는다(맵 루트는 런타임에 돌지 않는다).
    public Quaternion GhostRotation = Quaternion.identity;
    // 고스트 위치가 갱신된 직후 호출된다.
    // 요청 측이 생성된 고스트의 받침대처럼 위치에 종속된 시각 요소를 갱신할 때 사용한다.
    public Action<GameObject> OnGhostPositionUpdated;
    public Func<RaycastHit, bool> CanPlaceAt; // 배치 가능 여부 (그리드/검증 시스템이 제공)
    public Action<RaycastHit, Vector3> OnConfirmed; // (히트, 스냅 위치) 확정 시 실제 배치 수행
    // 놓을 수 없는 곳을 클릭했을 때 호출된다 — 거절 피드백(효과음 등)용. 선택(null 허용).
    // 이게 없으면 무효 클릭이 **아무 신호도 내지 않고 삼켜져** 플레이어에게는 클릭이 안 먹은 것처럼 보인다.
    // "왜 안 놓이는지"(도로·용암·점유·자원 부족)는 요청 측이 알지만, "거절됐다"는 사실은 매니저만 아는
    // 정보라 여기서 낸다. 사유별로 다른 반응이 필요해지면 인자를 붙인다(지금은 소비처가 소리 하나뿐).
    public Action OnRejected;
    // 커서가 배치 표면 위에 있는지 바뀔 때 호출된다(고스트를 켜고 끄는 것과 **같은 타이밍**). 선택(null 허용).
    //
    // 매니저는 자기 고스트만 숨길 수 있고 요청 측이 따로 띄운 미리보기(풋프린트 하이라이트·사거리 원 등)는
    // 모른다. 이 신호가 없으면 커서가 타일 밖으로 나갔을 때 **고스트만 사라지고 나머지는 마지막 타일에
    // 그대로 남는다** — 실제로 그렇게 새고 있었다. 표면을 벗어나면 Snap이 아예 호출되지 않으므로
    // 요청 측에는 갱신할 기회 자체가 없다.
    //
    // ⚠ **호출 순서는 API의 일부다**: `true`는 그 프레임의 `Snap` **뒤에** 오고, `false`는 `Snap`이
    // 돌지 않은 프레임에 온다. 즉 `true`를 받는 시점에는 요청 측 미리보기가 **이미 이번 프레임 위치로
    // 갱신돼 있다** — 그래서 `TowerPlacer`처럼 켜는 일을 `Snap`에 맡기고 `false`만 처리하는 구현이
    // 성립한다. 매니저에서 통지를 `Snap` 앞으로 옮기면 그런 소비처가 조용히 깨지므로(증상: 타일 밖에
    // 나갔다 돌아오면 하이라이트가 안 돌아옴) 순서를 바꾸려면 소비처를 함께 볼 것.
    public Action<bool> OnSurfaceHoverChanged;
    public Action OnEnded; // 배치 모드 종료(취소 또는 확정 후 복귀) 시 호출 — 프리뷰/고스트 부가물 정리용. 선택(null 허용)
    public bool KeepPlacingAfterConfirm; // 연속 배치 여부 (TBD)
}