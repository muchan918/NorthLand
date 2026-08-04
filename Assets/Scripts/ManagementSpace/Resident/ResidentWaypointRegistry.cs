using System.Collections.Generic;
using UnityEngine;

/// 씬에 심긴 <see cref="ResidentWaypoint"/>의 전역 목록. 주민 BT가 "어디로 갈 수 있나"를 묻는 유일한 출처다.
///
/// 왜 레지스트리인가: 주민 30명이 각자 목적지를 새로 뽑을 때마다 FindObjectsByType으로 씬을 긁으면
/// 탐색이 인원수만큼 돈다 → 대상이 스스로 등록/해제한다.
/// <see cref="GroupSelectableRegistry"/>와 같은 방식이고, 문서(Docs/ManagementArea/Resident.md §4)가
/// 월드 앵커 전반에 대해 정한 규약이기도 하다.
public static class ResidentWaypointRegistry
{
    private static readonly List<ResidentWaypoint> _waypoints = new();

    /// 후보를 고를 때 쓰는 재사용 버퍼. 호출마다 새 List를 만들면 주민 수 × 목적지 갱신 빈도만큼
    /// 할당이 발생한다 — 앰비언트 캐릭터가 GC를 만들 이유가 없다.
    private static readonly List<ResidentWaypoint> _usableBuffer = new();

    /// 플레이 세션 시작마다 비운다. 도메인 리로드가 꺼져 있으면 이전 세션의 죽은 항목이 남는다
    /// (<see cref="GroupSelectableRegistry"/>와 같은 이유).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _waypoints.Clear();
        _usableBuffer.Clear();
    }

    /// 등록 순서대로의 전체 목록. 비활성 항목도 포함하므로 소비처가 <see cref="ResidentWaypoint.IsUsable"/>을 본다.
    public static IReadOnlyList<ResidentWaypoint> Waypoints => _waypoints;

    public static void Register(ResidentWaypoint waypoint)
    {
        if (waypoint == null || _waypoints.Contains(waypoint))
        {
            return;
        }

        _waypoints.Add(waypoint);
    }

    /// 비활성화·파괴 시 해제(OnDisable). Unity는 파괴 시에도 OnDisable을 부르므로 이 한 쌍만으로 누수가 없다.
    public static void Unregister(ResidentWaypoint waypoint)
    {
        if (waypoint == null)
        {
            return;
        }

        _waypoints.Remove(waypoint);
    }

    /// 쓸 수 있는 웨이포인트 하나를 무작위로 고른다.
    ///
    /// 가중치를 두지 않는다 — 넓은 영역이 더 자주 뽑히게 하고 싶어질 수 있지만, 그러면 배치자가
    /// 반경으로 크기와 인기를 동시에 조절하게 되어 둘을 따로 못 만진다. 인기를 조절하고 싶으면
    /// 같은 자리에 웨이포인트를 하나 더 놓으면 된다.
    public static bool TryGetRandomWaypoint(out ResidentWaypoint waypoint)
    {
        waypoint = null;

        _usableBuffer.Clear();

        for (int i = 0; i < _waypoints.Count; i++)
        {
            ResidentWaypoint candidate = _waypoints[i];

            // 파괴된 항목(Unity 가짜 null)이 섞여 있을 수 있다 — OnDisable을 못 탄 경우의 방어.
            if (candidate == null || !candidate.IsUsable)
            {
                continue;
            }

            _usableBuffer.Add(candidate);
        }

        if (_usableBuffer.Count == 0)
        {
            return false;
        }

        waypoint = _usableBuffer[Random.Range(0, _usableBuffer.Count)];
        return true;
    }
}
