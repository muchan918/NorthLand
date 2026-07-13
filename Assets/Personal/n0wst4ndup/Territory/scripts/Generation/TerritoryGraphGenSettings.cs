using System;
using UnityEngine;

/// <summary>
/// 영토 그래프 절차 생성 파라미터 (TerritoryGraph.md §4.1).<br/>
/// 같은 (설정, 시드) 쌍은 항상 같은 그래프를 만든다 — <b>설정도 그래프 정체성의 일부</b>이므로
/// 결과를 로그로 남길 때는 시드와 함께 기록한다.
/// </summary>
[Serializable]
public class TerritoryGraphGenSettings
{
    [Tooltip("본진 포함 노드 수. GDD 상한 30 (초과 시 클램프+경고)")]
    public int NodeCount = 20;

    [Tooltip("산포 부채꼴 반지름 (본진=꼭짓점 기준)")]
    public float AreaRadius = 10f;

    [Tooltip("확장 부채꼴의 중심 방향(도). XZ 평면 기준 0=+X, 90=+Z. " +
             "GDD 공간 배치(전투 영역 ↔ 본진 ↔ 경영 확장)에서 전투 공간 반대쪽을 향하게 조정")]
    public float SectorCenterDegrees = 90f;

    [Range(10f, 360f)]
    [Tooltip("확장 부채꼴의 전체 각도(도). 본진에서 한쪽으로만 퍼지는 GDD 구조 — 180=반원, 360=전방향")]
    public float SectorAngleDegrees = 180f;

    [Tooltip("노드 간 최소 간격. 겹침 방지를 '보장'한다 (만족 불가 설정이면 점진 완화+경고)")]
    public float MinNodeSpacing = 2f;

    [Tooltip("바깥 편향 지수. 1=면적 균일 분포, 클수록 본진에서 먼 바깥 고리에 몰린다")]
    public float OutwardBias = 1.5f;

    [Range(0f, 1f)]
    [Tooltip("스패닝 트리 외 Delaunay 엣지 유지 확률 — 사이클(되돌아 잇는 연결) 밀도. 프루닝 단계에서 사용")]
    public float ExtraEdgeKeepRatio = 0.3f;
}
