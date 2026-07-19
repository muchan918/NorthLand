using System;
using UnityEngine;

/// "스킬을 어디에 시전할지" 지정하는 요청. PlacementRequest보다 가볍다 —
/// 스킬은 그리드 스냅·점유 검증이 필요 없고, 클릭한 위치 그대로 시전된다.
public class SkillTargetRequest
{
    public GameObject GhostPrefab; // 마우스를 따라다닐 범위 인디케이터
    public Action<Vector3> OnConfirmed; // 클릭 확정 시 시전 위치
    public Action OnEnded; // 타겟팅 종료(확정 또는 취소) 시 정리용. 선택(null 허용)
}
