using System;
using UnityEngine;

/// "무엇을 / 어디에 놓을 수 있는지 / 놓았을 때 무엇을 할지"를 캡슐화한 배치 요청. (요구사항 ①)
/// 타워·건물 등 배치물 종류가 늘어도 이 요청만 다르게 만들면 된다 → 확장성.
/// 배치 표면 히트(RaycastHit)를 그대로 넘겨, 그리드 스냅·검증·확정을 요청 측이 결정한다.
public class PlacementRequest
{
    public GameObject GhostPrefab; // 마우스를 따라다닐 프리뷰
    public Func<RaycastHit, Vector3> Snap; // 히트 → 배치 기준 위치(그리드 스냅). null이면 hit.point 사용
    public Func<RaycastHit, bool> CanPlaceAt; // 배치 가능 여부 (그리드/검증 시스템이 제공)
    public Action<RaycastHit, Vector3> OnConfirmed; // (히트, 스냅 위치) 확정 시 실제 배치 수행
    public Action OnEnded; // 배치 모드 종료(취소 또는 확정 후 복귀) 시 호출 — 프리뷰/고스트 부가물 정리용. 선택(null 허용)
    public bool KeepPlacingAfterConfirm; // 연속 배치 여부 (TBD)
}