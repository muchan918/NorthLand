using UnityEngine;

/// 스킬 범위 인디케이터(고스트). MouseManager가 SkillTargetRequest.GhostPrefab을 Instantiate해서
/// 마우스를 따라다니게 만든다. 반지름은 SkillManager의 현재 값을 그대로 따라간다(수동 동기화 불필요).
///
/// 이슈 #286 + 원기둥 아우라: 예전 LineRenderer 방식과 같은 구조(Awake에서 코드로 직접 생성)로 돌아가되,
/// 이번엔 "NorthLand/SkillAura" 셰이더를 쓰는 SkillAura 프리팹(원기둥)을 Instantiate한다.
/// 에디터에서 수동으로 자식을 미리 넣어두는 방식은 넣는 걸 깜빡하기 쉬워서(실제로 한 번 그랬다)
/// 코드가 직접 만들도록 바꿨다.
[DisallowMultipleComponent]
public class SkillRangeIndicator : MonoBehaviour
{
    const float k_UnitCylinderRadius = 0.5f; // Unity 기본 Cylinder 프리미티브의 로컬 반지름(고정값)
    const float k_UnitCylinderHalfHeight = 1f; // 〃 반높이(로컬 Y가 -1~+1이므로)

    [SerializeField] GameObject auraVisualPrefab; // SkillAura.prefab(원기둥 + 아우라 셰이더)

    void Awake()
    {
        float radius = SkillManager.Instance != null ? SkillManager.Instance.Radius : 3f;

        if (auraVisualPrefab == null)
        {
            Debug.LogError("[SkillRangeIndicator] auraVisualPrefab이 지정되지 않았습니다.");
            return;
        }

        var visual = Instantiate(auraVisualPrefab, transform).transform;

        // X/Z만 조정하고 Y(높이)는 프리팹에 저장된 값을 그대로 둔다 — 스킬 사거리가 커져도
        // 아우라 높이는 고정, 반경만 넓어지는 게 자연스럽다.
        float scaleXZ = radius / k_UnitCylinderRadius;
        Vector3 s = visual.localScale;
        visual.localScale = new Vector3(scaleXZ, s.y, scaleXZ);

        // 기본 Cylinder는 피봇이 중심이라 그대로 두면 아래 절반이 지면에 파묻힌다. 스케일된 반높이만큼
        // 올려 "고스트 루트 위치 = 원기둥 바닥"이 되게 한다(프리팹에 남아있던 x/z 오프셋도 여기서 제거).
        visual.localPosition = new Vector3(0f, k_UnitCylinderHalfHeight * s.y, 0f);
    }
}