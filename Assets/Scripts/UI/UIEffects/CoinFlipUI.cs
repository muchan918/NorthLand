using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class CoinFlipUI : MonoBehaviour
{
    [SerializeField] private Image coinImage;
    [SerializeField] private Sprite frontSprite;
    [SerializeField] private Sprite backSprite;
    [SerializeField] private float flipDuration = 0.5f;

    private bool isFront = true;
    private bool isFlipping;

    private void Start()
    {
        if (coinImage == null || frontSprite == null || backSprite == null)
        {
            Debug.LogError("[CoinFlipUI] Image 또는 앞/뒷면 Sprite가 연결되지 않았습니다.", this);
            enabled = false;
            return;
        }

        if (DayNightManager.Instance == null)
        {
            Debug.LogError("[CoinFlipUI] DayNightManager를 찾을 수 없습니다.", this);
            enabled = false;
            return;
        }

        DayNightManager.Instance.OnDayToNight += HandleDayToNight;
        DayNightManager.Instance.OnNightToDay += HandleNightToDay;

        ApplyPhase(DayNightManager.Instance.CurrentPhase);
    }

    private void OnDestroy()
    {
        if (DayNightManager.Instance != null)
        {
            DayNightManager.Instance.OnDayToNight -= HandleDayToNight;
            DayNightManager.Instance.OnNightToDay -= HandleNightToDay;
        }
    }

    public async UniTaskVoid Flip()
    {
        if (isFlipping)
        {
            return;
        }

        isFlipping = true;

        float elapsed = 0f;
        bool spriteChanged = false;

        try
        {
            while (elapsed < flipDuration)
            {
                elapsed += Time.unscaledDeltaTime;

                float ratio = Mathf.Clamp01(elapsed / flipDuration);
                float angle = Mathf.Lerp(0f, 180f, ratio);

                if (!spriteChanged && angle >= 90f)
                {
                    isFront = !isFront;
                    coinImage.sprite = isFront ? frontSprite : backSprite;

                    spriteChanged = true;
                }

                float visibleAngle = angle <= 90f ? angle : 180f - angle;

                transform.localRotation = Quaternion.Euler(0f, visibleAngle, 0f);

                await UniTask.Yield(PlayerLoopTiming.Update, this.GetCancellationTokenOnDestroy());
            }
        }
        finally
        {
            transform.localRotation = Quaternion.identity;
            isFlipping = false;
        }
    }

    private void HandleDayToNight()
    {
        if (isFront)
        {
            Flip().Forget();
        }
    }

    private void HandleNightToDay()
    {
        if (!isFront)
        {
            Flip().Forget();
        }
    }

    private void ApplyPhase(DayNightManager.Phase phase)
    {
        isFront = phase == DayNightManager.Phase.Day;
        coinImage.sprite = isFront ? frontSprite : backSprite;
        transform.localRotation = Quaternion.identity;
    }

}
