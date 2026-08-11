using UnityEngine;

// #103 스킬 시스템 수동 검증 하네스. 이 프로젝트엔 NUnit 테스트가 없어(unity-cli-guide.md 참고)
// TerritoryTest와 동일한 컨벤션 — PASS/FAIL을 콘솔 로그로 남기고 Play 모드에서 눈으로 확인한다.
public class SkillManagerTest : MonoBehaviour
{
    private void Start()
    {
        if (SkillManager.Instance == null)
        {
            Debug.LogError("[SkillTest] FAIL: SkillManager 없음");
            return;
        }
        if (DayNightManager.Instance == null)
        {
            Debug.LogError("[SkillTest] FAIL: DayNightManager 없음");
            return;
        }

        var dummy = new GameObject("SkillTest_Dummy").AddComponent<DummyDamageable>();
        dummy.transform.position = transform.position;

        // 프로젝트 Physics 설정(Auto Sync Transforms)이 꺼져 있어(ProjectSettings/DynamicsManager.asset),
        // 방금 옮긴 트랜스폼이 물리 엔진에 자동 반영되지 않는다. 같은 프레임에서 바로 OverlapSphere로
        // 이 더미를 찾아야 하므로 수동으로 동기화한다 — 안 하면 CastAt이 항상 대상을 못 찾는다.
        Physics.SyncTransforms();

        // 1. 낮에는 시전이 막혀야 한다.
        if (DayNightManager.Instance.CurrentPhase != DayNightManager.Phase.Day)
            Debug.LogWarning("[SkillTest] 이미 밤 상태로 시작 — 낮 게이팅 검증은 건너뜀");
        else if (SkillManager.Instance.CastAt(dummy.transform.position))
            Debug.LogError("[SkillTest] FAIL: 낮인데 CastAt이 성공했다");
        else
            Debug.Log("[SkillTest] PASS: 낮에는 CastAt이 차단됨");

        // 2. 밤으로 전환 후 시전 → 범위 내 대상이 데미지를 받아야 한다.
        if (DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day)
            DayNightManager.Instance.EndDay();

        bool firstCast = SkillManager.Instance.CastAt(dummy.transform.position);
        if (firstCast && dummy.DamageTaken > 0f)
            Debug.Log($"[SkillTest] PASS: 밤 시전 성공, 데미지={dummy.DamageTaken}");
        else
            Debug.LogError($"[SkillTest] FAIL: 밤 시전 실패 또는 데미지 없음 (성공={firstCast}, 데미지={dummy.DamageTaken})");

        // 3. 쿨다운 중 재시전은 막혀야 한다.
        float damageAfterFirstCast = dummy.DamageTaken;
        bool secondCast = SkillManager.Instance.CastAt(dummy.transform.position);
        if (!secondCast && dummy.DamageTaken == damageAfterFirstCast)
            Debug.Log("[SkillTest] PASS: 쿨다운 중 재시전 차단됨");
        else
            Debug.LogError("[SkillTest] FAIL: 쿨다운 중인데 재시전이 성공했다");

        // 검증 과정에서 소모한 쿨다운을 리셋 — 안 그러면 Play 시작 직후 스킬 버튼이
        // 실제 쿨다운(기본 5초)만큼 비활성 상태로 보여 인터랙티브 테스트를 방해한다.
        SkillManager.Instance.RefillChargesNow();

        Destroy(dummy.gameObject);
    }
}
