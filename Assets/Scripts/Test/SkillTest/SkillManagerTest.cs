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

        // 3. 충전이 남아 있는 동안은 연달아 시전되고, 소진되면 막혀야 한다(#319).
        // 최대 충전은 추가시전 보상 레벨에 따라 달라지므로(세이브 복원 시 2 이상일 수 있다)
        // 상수로 두지 않고 MaxCharges에서 읽는다 — 여기서 1을 가정하면 정상 동작이 FAIL로 찍힌다.
        int maxCharges = SkillManager.Instance.MaxCharges;
        bool burstSucceeded = true;
        for (int i = 1; i < maxCharges; i++)   // 1발은 위 2번에서 이미 썼다
            burstSucceeded &= SkillManager.Instance.CastAt(dummy.transform.position);

        float damageAfterBurst = dummy.DamageTaken;
        bool overCast = SkillManager.Instance.CastAt(dummy.transform.position);

        if (burstSucceeded && !overCast && dummy.DamageTaken == damageAfterBurst)
            Debug.Log($"[SkillTest] PASS: 충전 {maxCharges}발 연속 시전 후 차단됨");
        else
            Debug.LogError($"[SkillTest] FAIL: 충전 소진 판정 이상 (연속 성공={burstSucceeded}, 초과 시전={overCast})");

        // 4. 공중 유닛 회귀(#398): 시전은 지면인데 대상은 그 위 8f(부양 6f + 비행 고도 4f에 근사).
        // 위 1~3번은 더미를 시전 지점과 같은 높이에 두어 수직차가 0이라, 판정이 구체든 캡슐이든 통과한다
        // — 그래서 감전이 공중 유닛을 못 때리던 버그를 한 번도 잡지 못했다. 이 케이스가 그 축을 지킨다.
        SkillManager.Instance.RefillChargesNow();   // 3번에서 충전을 전부 소진했다

        var airDummy = new GameObject("SkillTest_AirDummy").AddComponent<DummyDamageable>();
        airDummy.transform.position = transform.position + Vector3.up * 8f;
        Physics.SyncTransforms();   // 위 26번 줄과 같은 이유 — Auto Sync Transforms가 꺼져 있다

        bool airCast = SkillManager.Instance.CastAt(transform.position);
        if (airCast && airDummy.DamageTaken > 0f)
            Debug.Log($"[SkillTest] PASS: 공중 대상 적중, 데미지={airDummy.DamageTaken}");
        else
            Debug.LogError($"[SkillTest] FAIL: 공중 대상 미적중 (시전={airCast}, 데미지={airDummy.DamageTaken}) — SkillHitScan 수직 범위 확인");

        Destroy(airDummy.gameObject);

        // 검증 과정에서 소모한 충전을 되돌린다 — 안 그러면 Play 시작 직후 스킬 버튼이
        // 재충전 간격(씬 값 10초)만큼 비활성 상태로 보여 인터랙티브 테스트를 방해한다.
        SkillManager.Instance.RefillChargesNow();

        Destroy(dummy.gameObject);
    }
}
