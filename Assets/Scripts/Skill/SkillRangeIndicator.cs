using UnityEngine;

/// 스킬 범위 인디케이터(고스트). MouseManager가 SkillTargetRequest.GhostPrefab을 Instantiate해서
/// 마우스를 따라다니게 만든다. 반지름은 SkillManager의 현재 값을 그대로 따라간다(수동 동기화 불필요).
///
/// 이슈 #286 + 원기둥 아우라: 예전 LineRenderer 방식과 같은 구조(Awake에서 코드로 직접 생성)로 돌아가되,
/// 시각 프리팹을 Instantiate하고, 프리팹의 기준 반경을 현재 스킬 반경에 맞춰 스케일한다.
/// 에디터에서 수동으로 자식을 미리 넣어두는 방식은 넣는 걸 깜빡하기 쉬워서(실제로 한 번 그랬다)
/// 코드가 직접 만들도록 바꿨다.
[DisallowMultipleComponent]
public class SkillRangeIndicator : MonoBehaviour
{
    // 바닥 캡을 시전 평면보다 아주 살짝 띄운다. 시전 높이(SkillButtonView._castHeight)를 도로 타일
    // 윗면에 정확히 맞추면 캡과 지면이 완전 공면이 되어 z-파이팅이 난다 — 게다가 캡은 수직 페이드가
    // 0인 가장 진한 부분이고 ZWrite Off라 정렬로도 안 풀린다. 구 구현의 y=0.05f, RangeCircle.k_YOffset(0.06f)이
    // 같은 이유로 존재했다.
    [SerializeField] GameObject auraVisualPrefab;
    [Tooltip("시각 프리팹이 localScale 1일 때 표현하는 월드 반경")]
    [Min(0.01f)]
    [SerializeField] float referenceRadius = 1f;
    [Tooltip("인디케이터를 시전 평면에서 위로 띄우는 로컬 Y 오프셋")]
    [SerializeField] float yOffset = 0.05f;

    void Awake()
    {
        float radius = SkillManager.Instance != null ? SkillManager.Instance.Radius : 3f;

        if (auraVisualPrefab == null)
        {
            Debug.LogError("[SkillRangeIndicator] auraVisualPrefab이 지정되지 않았습니다.");
            return;
        }

        Transform visual = Instantiate(auraVisualPrefab, transform).transform;

        Vector3 s = visual.localScale;

        // 여러 ParticleSystem의 모양을 찌그러뜨리지 않도록 씬에서 검증한 것과 같은 균일 스케일을 쓴다.
        // SkillIndicator는 기준 반경 1.2에서 scale 5일 때 반경 6을 표현한다.
        float scale = radius / Mathf.Max(referenceRadius, 0.01f);
        visual.localScale = s * scale;
        visual.localPosition = new Vector3(0f, yOffset, 0f);
    }
}
