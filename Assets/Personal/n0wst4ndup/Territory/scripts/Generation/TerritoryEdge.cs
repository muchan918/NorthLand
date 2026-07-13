using System;

/// <summary>
/// 무방향 엣지 — 생성 결과의 "형태" 표현.<br/>
/// 항상 A &lt; B로 정규화해 (3,7)과 (7,3)이 같은 엣지로 취급되고,
/// 정렬·비교·중복 제거가 결정적이 된다 — rng 소비 루프의 순회 순서 고정(재현성)의 전제.
/// </summary>
public readonly struct TerritoryEdge : IEquatable<TerritoryEdge>, IComparable<TerritoryEdge>
{
    public readonly int A;
    public readonly int B;

    public TerritoryEdge(int a, int b)
    {
        A = Math.Min(a, b);
        B = Math.Max(a, b);
    }

    public bool Equals(TerritoryEdge other) => A == other.A && B == other.B;

    public override bool Equals(object obj) => obj is TerritoryEdge other && Equals(other);

    public override int GetHashCode() => A * 397 + B;

    /// <summary>(A, B) 오름차순.</summary>
    public int CompareTo(TerritoryEdge other) =>
        A != other.A ? A.CompareTo(other.A) : B.CompareTo(other.B);

    public override string ToString() => $"({A}-{B})";
}
