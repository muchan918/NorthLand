using System.Collections.Generic;
using UnityEngine;

// 패턴 Key별 마지막 사용 시각 보관소(#233). EnemyPatternGateCondition과
// EnemyMarkPatternUsedAction이 짝을 이뤄 이걸 통해 1회 한정·쿨다운 게이트를 판정한다.
//
// MonoBehaviour가 아니다 — 프리팹에 컴포넌트를 하나 더 요구하지 않도록 EnemyAgent가 내부 필드로 든다.
// EnemyAgent는 무상태 파사드가 원칙이고 이 기록이 유일한 예외다
// (Docs/Monster/Boss/BossNodeReference.md 「대상 주입 방식」).
//
// 네임스페이스를 두지 않는다 — 커스텀 노드와 같은 규약이므로 클래스 이름이 전역에서 유일해야 한다.
public class EnemyPatternMemory
{
    private readonly Dictionary<string, float> lastUsedTime = new Dictionary<string, float>();

    // 게이트 통과 여부. 한 번도 쓰지 않았으면 항상 참.
    // cooldownSeconds < 0이면 1회 한정 — 한 번 쓰면 이후 영구 거짓이다.
    // Key가 비어 있으면 거짓으로 처리한다(게이트 없는 패턴이 무한 발동하는 쪽보다 안전하다).
    public bool IsReady(string key, float cooldownSeconds)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (!lastUsedTime.TryGetValue(key, out float usedAt))
        {
            return true;
        }

        if (cooldownSeconds < 0f)
        {
            return false;
        }

        return Time.time - usedAt >= cooldownSeconds;
    }

    // Time.time은 스케일드 타임이므로 게임 배속(x2/x4)에 비례해 쿨다운도 빨리 돈다.
    // 배속과 패턴 타이밍의 관계는 BT의 Wait 내장 노드도 같은 성질이라 전체를 함께 정해야 한다
    // (BossNodeReference.md 「미확정 / TODO」).
    public void MarkUsed(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        lastUsedTime[key] = Time.time;
    }
}
