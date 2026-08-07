using System.Collections.Generic;
using UnityEngine;

/// 씬에 심긴 <see cref="ResidentDoorPoint"/>의 전역 목록(#276, §4).
/// <see cref="ResidentWaypointRegistry"/>와 같은 방식 — 대상이 스스로 등록/해제한다.
public static class ResidentDoorPointRegistry
{
    private static readonly List<ResidentDoorPoint> _points = new();

    /// 후보를 고를 때 쓰는 재사용 버퍼. 밤 전환마다 주민 수만큼 질의가 들어오므로 할당을 만들지 않는다.
    private static readonly List<ResidentDoorPoint> _usableBuffer = new();

    public static IReadOnlyList<ResidentDoorPoint> Points => _points;

    /// 플레이 세션 시작마다 비운다. 도메인 리로드가 꺼져 있으면 이전 세션의 죽은 항목이 남는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _points.Clear();
        _usableBuffer.Clear();
    }

    public static void Register(ResidentDoorPoint point)
    {
        if (point == null || _points.Contains(point))
        {
            return;
        }

        _points.Add(point);
    }

    public static void Unregister(ResidentDoorPoint point)
    {
        if (point == null)
        {
            return;
        }

        _points.Remove(point);
    }

    /// 가장 가까운 문. R8 귀가가 쓴다 — **건물 타입을 판정하지 않는다**(§4).
    public static bool TryGetNearest(Vector3 from, out ResidentDoorPoint nearest)
    {
        nearest = null;

        float bestSqr = float.PositiveInfinity;

        for (int i = 0; i < _points.Count; i++)
        {
            ResidentDoorPoint candidate = _points[i];

            // 파괴된 항목(Unity 가짜 null)이 섞여 있을 수 있다 — OnDisable을 못 탄 경우의 방어.
            if (candidate == null || !candidate.IsUsable)
            {
                continue;
            }

            // 높이는 무시한다. 경영 공간은 사실상 평면이고, Y를 세면 지대가 다른 문이 부당하게 멀어진다.
            Vector3 delta = candidate.Position - from;
            delta.y = 0f;

            float sqr = delta.sqrMagnitude;

            if (sqr >= bestSqr)
            {
                continue;
            }

            bestSqr = sqr;
            nearest = candidate;
        }

        return nearest != null;
    }

    /// 쓸 수 있는 문을 <paramref name="buffer"/>에 채운다. 아침 배분(§11.11 ①)이 쓴다.
    ///
    /// 호출부가 버퍼를 넘기는 이유: 스포너가 이 목록을 **섞어서** 소비하므로 내부 재사용 버퍼를
    /// 그대로 내주면 다음 질의가 그 순서에 영향을 받는다.
    public static int CollectUsable(List<ResidentDoorPoint> buffer)
    {
        if (buffer == null)
        {
            return 0;
        }

        buffer.Clear();

        for (int i = 0; i < _points.Count; i++)
        {
            ResidentDoorPoint candidate = _points[i];

            if (candidate != null && candidate.IsUsable)
            {
                buffer.Add(candidate);
            }
        }

        return buffer.Count;
    }
}
