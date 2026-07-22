using UnityEngine;

/// 스킬 범위 인디케이터(고스트). MouseManager가 SkillTargetRequest.GhostPrefab을 Instantiate해서
/// 마우스를 따라다니게 만든다(TowerPlacer.CreateRangeIndicator와 동일한 LineRenderer 원 방식).
/// 반지름은 SkillManager의 현재 값을 그대로 따라간다(수동 동기화 불필요).
public class SkillRangeIndicator : MonoBehaviour
{
    [SerializeField] Color validColor = new Color(0.3f, 1f, 0.4f, 0.85f);   // 도로 위 = 시전 가능
    [SerializeField] Color invalidColor = new Color(1f, 0.3f, 0.25f, 0.85f); // 도로 밖 = 시전 불가
    [SerializeField] int segments = 48;

    Material runtimeMaterial;
    LineRenderer lr; // SetValid에서 정점색을 갱신하기 위해 캐시

    private void Awake()
    {
        float radius = SkillManager.Instance != null ? SkillManager.Instance.Radius : 3f;

        lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.widthMultiplier = 3f;
        // 타겟팅마다 Instantiate되는 고스트라 매번 새 Material이 생긴다 — GameObject가 Destroy될 때
        // 같이 정리하기 위해 참조를 들고 있는다(OnDestroy 참고, PR#115 리뷰 지적).
        runtimeMaterial = new Material(Shader.Find("Sprites/Default")); // 언릿, 정점색으로 tint
        lr.sharedMaterial = runtimeMaterial;
        lr.startColor = lr.endColor = validColor;

        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0.05f, Mathf.Sin(angle) * radius));
        }
    }

    // 도로 위(시전 가능)=초록 / 그 외=빨강. MouseManager가 매 프레임 커서 밑 타일 판정으로 호출한다.
    public void SetValid(bool valid)
    {
        if (lr != null) lr.startColor = lr.endColor = valid ? validColor : invalidColor;
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }
}
