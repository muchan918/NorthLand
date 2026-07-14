using UnityEngine;

/// <summary>
/// 영토 생성용 2D 기하 판정 유틸. float 좌표를 double로 승격해 계산한다 —
/// 30노드 + 최소 간격 보장 조건에서는 이것으로 충분하며 정밀 술어(exact predicate)까지는 불필요.
/// </summary>
public static class TerritoryGeometry
{
    /// <summary>
    /// 부호 있는 외적(평행사변형 면적). &gt;0이면 a→b→c가 반시계(CCW), &lt;0이면 시계,
    /// 0에 가까우면 세 점이 거의 일직선(퇴화).
    /// </summary>
    public static double Orientation(Vector2 a, Vector2 b, Vector2 c)
    {
        double abx = (double)b.x - a.x;
        double aby = (double)b.y - a.y;
        double acx = (double)c.x - a.x;
        double acy = (double)c.y - a.y;
        return abx * acy - aby * acx;
    }

    /// <summary>
    /// p가 삼각형 a,b,c(반드시 CCW 순서)의 외접원 내부에 있으면 양수, 원 위면 0, 밖이면 음수.<br/>
    /// 외접원의 중심·반지름을 직접 구하지 않고 p 기준으로 평행이동한 3×3 행렬식으로 판정한다
    /// — 뺄셈을 먼저 해 큰 수끼리의 차 계산을 피하는 쪽이 수치적으로 안전하다.
    /// </summary>
    public static double InCircle(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
    {
        double ax = (double)a.x - p.x, ay = (double)a.y - p.y;
        double bx = (double)b.x - p.x, by = (double)b.y - p.y;
        double cx = (double)c.x - p.x, cy = (double)c.y - p.y;

        double a2 = ax * ax + ay * ay;
        double b2 = bx * bx + by * by;
        double c2 = cx * cx + cy * cy;

        return ax * (by * c2 - b2 * cy)
             - ay * (bx * c2 - b2 * cx)
             + a2 * (bx * cy - by * cx);
    }

    /// <summary>
    /// 두 선분이 내부에서 "진짜로" 교차하는가(proper intersection).
    /// 끝점을 공유하거나 끝점이 상대 선분 위에 스치는 경우는 false —
    /// 평면 그래프(엣지 교차 없음, TerritoryGraph.md §4.1) 검증용.
    /// </summary>
    public static bool SegmentsProperlyIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        double d1 = Orientation(q1, q2, p1);
        double d2 = Orientation(q1, q2, p2);
        double d3 = Orientation(p1, p2, q1);
        double d4 = Orientation(p1, p2, q2);

        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
}
