using System;
using UnityEngine;

/// "무엇을 / 어디에 놓을 수 있는지 / 놓았을 때 무엇을 할지"를 캡슐화한 배치 요청. (요구사항 ①)
/// 타워·건물·병사 등 배치물 종류가 늘어도 이 요청만 다르게 만들면 된다 → 확장성.
public class PlacementRequest
{
    public GameObject GhostPrefab; // 마우스를 따라다닐 프리뷰
    public Func<Vector3, bool> CanPlaceAt; // 배치 가능 여부 (그리드/검증 시스템이 제공)
    public Action<Vector3> OnConfirmed; // 유효 위치에서 확정 시 실제 배치 수행
    public bool KeepPlacingAfterConfirm; // 연속 배치 여부 (TBD)
}