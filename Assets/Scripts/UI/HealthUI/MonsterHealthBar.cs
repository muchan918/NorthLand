using NorthLand.Combat;
using UnityEngine;
using UnityEngine.UI;

// 몬스터 프리팹(Phantom/Shadow)의 자식 World Space Canvas에 붙는 개별 체력바.
// Enemy.OnHpChanged를 구독해 갱신하며, 몬스터가 죽어 GameObject가 파괴되면 자식인 이 오브젝트도 함께 정리된다.
public class MonsterHealthBar : MonoBehaviour
{
    [SerializeField] Slider slider;

    Enemy enemy;
    Camera mainCamera;

    void Awake()
    {
        enemy = GetComponentInParent<Enemy>();
        mainCamera = Camera.main;

        if (enemy == null || slider == null)
        {
            Debug.LogWarning("[MonsterHealthBar] Enemy 또는 Slider 참조가 없어 비활성화합니다.", this);
            gameObject.SetActive(false);
            return;
        }

        enemy.OnHpChanged += UpdateBar;
        UpdateBar(enemy.CurrentHp, enemy.MaxHp);
    }

    void OnDestroy()
    {
        if (enemy != null)
            enemy.OnHpChanged -= UpdateBar;
    }

    // 몬스터가 이동/회전해도 체력바는 항상 카메라를 향하도록 고정(빌보드).
    void LateUpdate()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        transform.rotation = mainCamera.transform.rotation;
    }

    void UpdateBar(float current, float max)
    {
        // MaxHp==0은 EnemyAsset 스탯 미설정(WL-001) 상태 — 바 자체를 숨긴다.
        gameObject.SetActive(max > 0f);
        slider.value = max > 0f ? current / max : 0f;
    }
}
