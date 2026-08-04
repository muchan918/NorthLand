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
    // 바닥 캡을 시전 평면보다 아주 살짝 띄운다. 시전 높이(SkillButtonView._castHeight)를 도로 타일
    // 윗면에 정확히 맞추면 캡과 지면이 완전 공면이 되어 z-파이팅이 난다 — 게다가 캡은 수직 페이드가
    // 0인 가장 진한 부분이고 ZWrite Off라 정렬로도 안 풀린다. 구 구현의 y=0.05f, RangeCircle.k_YOffset(0.06f)이
    // 같은 이유로 존재했다.
    const float k_GroundClearance = 0.05f;

    [SerializeField] GameObject auraVisualPrefab; // SkillAura.prefab(원기둥 + 아우라 셰이더)

    void Awake()
    {
        float radius = SkillManager.Instance != null ? SkillManager.Instance.Radius : 3f;

        if (auraVisualPrefab == null)
        {
            Debug.LogError("[SkillRangeIndicator] auraVisualPrefab이 지정되지 않았습니다.");
            return;
        }

        Transform visual = Instantiate(auraVisualPrefab, transform).transform;

        // 메시 규격(반경·바닥 높이)은 메시에서 직접 읽는다 — 상수로 적어 두면 프리팹 메시를 교체했을 때
        // 컴파일 에러도 로그도 없이 반경·바닥 정렬만 조용히 어긋난다(PR#289 리뷰).
        // 셰이더가 쓰는 _LocalRadius/_LocalHeight(SkillAura.mat)는 여전히 수동 authoring이라, 메시를
        // 교체하면 그쪽은 함께 고쳐야 한다(SkillAura.shader 헤더 주석 참고).
        MeshFilter meshFilter = visual.GetComponentInChildren<MeshFilter>();
        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (mesh == null)
        {
            Debug.LogError("[SkillRangeIndicator] auraVisualPrefab에 MeshFilter/메시가 없습니다.", auraVisualPrefab);
            return;
        }

        Bounds bounds = mesh.bounds;
        Vector3 s = visual.localScale;

        // X/Z만 조정하고 Y(높이)는 프리팹에 저장된 값을 그대로 둔다 — 스킬 사거리가 커져도
        // 아우라 높이는 고정, 반경만 넓어지는 게 자연스럽다.
        float meshRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        float scaleXZ = meshRadius > Mathf.Epsilon ? radius / meshRadius : s.x; // 퇴화 메시면 프리팹 스케일 유지
        visual.localScale = new Vector3(scaleXZ, s.y, scaleXZ);

        // 메시 바닥(bounds.min.y)을 고스트 루트 높이에 맞춘다. 기본 Cylinder처럼 피봇이 중심인 메시는
        // 그만큼 위로 올라가고, 피봇이 이미 바닥인 메시면 오프셋 0이 나와 그대로 앉는다
        // (프리팹에 남아있던 x/z 오프셋도 여기서 함께 제거된다).
        visual.localPosition = new Vector3(0f, -bounds.min.y * s.y + k_GroundClearance, 0f);
    }
}