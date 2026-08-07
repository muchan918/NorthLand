using System.Collections.Generic;
using UnityEngine;

/// 상대별 조우 재시도 쿨다운(#276, §7.1). 주민 1명이 하나씩 든다.
///
/// MonoBehaviour가 아니라 plain class로 <see cref="Resident"/>가 내부 필드로 든다 —
/// 프리팹에 컴포넌트를 하나 더 요구하지 않기 위해서다(<c>EnemyPatternMemory</c>와 같은 판단).
///
/// **왜 필요한가**: 없으면 나란히 걷는 두 명이 구간마다 조우 판정을 반복해 결국 붙는다. 그리고 해산
/// 직후 같은 상대와 다시 성립하면 두 명이 영원히 인사만 한다.
///
/// 실패와 해산을 같은 표에 기록하고 **쿨다운 길이만 다르게** 준다. 두 표로 나누면 "실패 쿨다운 중에
/// 해산 쿨다운이 짧게 덮어쓴다" 같은 조합을 따져야 한다.
public sealed class ResidentEncounterMemory
{
    /// 상대 InstanceID → 다시 시도해도 되는 절대 시각. 항목 수는 씬의 주민 수로 상한이 잡히므로
    /// 따로 비우지 않는다.
    private readonly Dictionary<int, float> readyAt = new Dictionary<int, float>();

    /// 이 상대에게 지금 말을 걸어도 되는가.
    public bool IsReady(Resident other)
    {
        if (other == null)
        {
            return false;
        }

        return !readyAt.TryGetValue(other.GetInstanceID(), out float time) || Time.time >= time;
    }

    /// 이 상대를 <paramref name="cooldownSeconds"/> 동안 후보에서 뺀다.
    ///
    /// 더 늦은 시각으로만 갱신한다 — 짧은 쿨다운이 긴 쿨다운을 덮어써서 조기 재시도가 열리는 것을 막는다.
    public void Mark(Resident other, float cooldownSeconds)
    {
        if (other == null || cooldownSeconds <= 0f)
        {
            return;
        }

        int key = other.GetInstanceID();
        float next = Time.time + cooldownSeconds;

        readyAt[key] = readyAt.TryGetValue(key, out float existing) && existing > next ? existing : next;
    }
}
