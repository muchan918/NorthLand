using NorthLand.Combat;
using UnityEngine;
using UnityEngine.UI;

// 화면 상단 오버레이 Canvas에 배치되는 본진(PlayerBase) 체력바.
// PlayerBase는 낮에는 존재하지 않고 밤에 MonsterSpawn이 성문(BaseGate)을 스폰할 때 생성되므로,
// 이미 스폰된 경우(Instance)와 앞으로 스폰될 경우(OnBaseSpawned) 둘 다 대응한다.
public class BaseHealthBar : MonoBehaviour
{
    [SerializeField] Slider slider;

    PlayerBase playerBase;

    void Awake()
    {
        gameObject.SetActive(false); // 본진이 스폰되기 전에는 숨김

        if (slider == null)
        {
            Debug.LogWarning("[BaseHealthBar] Slider 참조가 없습니다.", this);
            return;
        }

        if (PlayerBase.Instance != null)
            Bind(PlayerBase.Instance);
        else
            PlayerBase.OnBaseSpawned += Bind;
    }

    void OnDestroy()
    {
        PlayerBase.OnBaseSpawned -= Bind;
        if (playerBase != null)
            playerBase.OnHpChanged -= UpdateBar;
    }

    void Bind(PlayerBase pb)
    {
        if (playerBase != null)
            playerBase.OnHpChanged -= UpdateBar;

        playerBase = pb;
        playerBase.OnHpChanged += UpdateBar;
        gameObject.SetActive(true);
        UpdateBar(playerBase.CurrentHp, playerBase.MaxHp);
    }

    void UpdateBar(float current, float max)
    {
        slider.value = max > 0f ? current / max : 0f;
    }
}
