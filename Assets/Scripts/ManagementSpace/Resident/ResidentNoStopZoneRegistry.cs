using System.Collections.Generic;
using UnityEngine;

/// 씬에 심긴 <see cref="ResidentNoStopZone"/>의 전역 목록(#332). 주민 BT가 "여기서 멈춰도 되나"를
/// 묻는 유일한 출처다.
///
/// <see cref="ResidentWaypointRegistry"/>·<see cref="ResidentRegistry"/>와 같은 방식이다 —
/// 대상이 스스로 등록/해제하고, 소비처는 매 질의마다 씬을 훑지 않는다.
///
/// 존은 많아야 5~10개고 질의는 **이동 구간 경계에서만** 돈다(대화·춤 판정과 같은 박자). 존이 하나도
/// 없으면 <see cref="Contains"/>가 즉시 빠지므로, 이 기능을 쓰지 않는 씬에서는 비용이 0이다.
public static class ResidentNoStopZoneRegistry
{
    private static readonly List<ResidentNoStopZone> _zones = new();

    /// 등록 순서대로의 전체 목록. 비활성 항목이 섞일 수 있으므로 소비처가
    /// <see cref="ResidentNoStopZone.IsUsable"/>을 본다.
    public static IReadOnlyList<ResidentNoStopZone> Zones => _zones;

    /// 플레이 세션 시작마다 비운다. 도메인 리로드가 꺼져 있으면 이전 세션의 죽은 항목이 남는다
    /// (<see cref="ResidentWaypointRegistry"/>와 같은 이유).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _zones.Clear();
    }

    public static void Register(ResidentNoStopZone zone)
    {
        if (zone == null || _zones.Contains(zone))
        {
            return;
        }

        _zones.Add(zone);
    }

    /// 비활성화·파괴 시 해제(OnDisable). Unity는 파괴 시에도 OnDisable을 부르므로 이 한 쌍만으로 누수가 없다.
    public static void Unregister(ResidentNoStopZone zone)
    {
        if (zone == null)
        {
            return;
        }

        _zones.Remove(zone);
    }

    /// 이 지점이 정지 금지 구역 안인가.
    ///
    /// 겹친 존을 따로 다루지 않는다 — L자 골목처럼 상자 하나로 안 되는 모양은 **여러 개를 겹쳐 그리는 것**이
    /// 정상 저작 방식이고, 하나라도 걸리면 금지다.
    public static bool Contains(Vector3 worldPoint)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            ResidentNoStopZone zone = _zones[i];

            // 파괴된 항목(Unity 가짜 null)이 섞여 있을 수 있다 — OnDisable을 못 탄 경우의 방어.
            if (zone == null || !zone.IsUsable)
            {
                continue;
            }

            if (zone.Contains(worldPoint))
            {
                return true;
            }
        }

        return false;
    }
}
