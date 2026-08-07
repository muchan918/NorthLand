using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class CoinFlipUI : MonoBehaviour
{
    [SerializeField] private Image coinImage;
    [SerializeField] private Sprite frontSprite;
    [SerializeField] private Sprite backSprite;
    [SerializeField] private float flipDuration = 0.5f;

    private bool isFront;
    private bool isFlipping;

    // 애니메이션 중 페이즈가 다시 바뀌었을 때 사용할 최종 목표
    private DayNightManager.Phase targetPhase;
    private DayNightManager dayNightManager;

    private void Start()
    {
        if (coinImage == null || frontSprite == null || backSprite == null)
        {
            Debug.LogError(
                "[CoinFlipUI] coinImage 또는 앞/뒷면 Sprite가 연결되지 않았습니다.",
                this);

            enabled = false;
            return;
        }

        dayNightManager = DayNightManager.Instance;

        if (dayNightManager == null)
        {
            Debug.LogError("[CoinFlipUI] DayNightManager를 찾을 수 없습니다.", this);
            enabled = false;
            return;
        }

        dayNightManager.OnDayToNight += HandleDayToNight;
        dayNightManager.OnNightToDay += HandleNightToDay;

        // 시작할 때 현재 페이즈와 즉시 동기화
        ApplyPhase(dayNightManager.CurrentPhase);
    }

    private void OnDestroy()
    {
        if (dayNightManager == null)
        {
            return;
        }

        dayNightManager.OnDayToNight -= HandleDayToNight;
        dayNightManager.OnNightToDay -= HandleNightToDay;
    }

    private void HandleDayToNight()
    {
        ChangePhaseAsync(DayNightManager.Phase.Night).Forget();
    }

    private void HandleNightToDay()
    {
        ChangePhaseAsync(DayNightManager.Phase.Day).Forget();
    }

    private async UniTaskVoid ChangePhaseAsync(DayNightManager.Phase phase)
    {
        targetPhase = phase;

        if (isFlipping)
        {
            return;
        }

        isFlipping = true;

        try
        {
            // 애니메이션 중 목표 페이즈가 바뀌면 다시 뒤집어서 최종 상태를 맞춤
            while (isFront != IsFrontPhase(targetPhase))
            {
                await FlipOnceAsync(targetPhase);
            }
        }
        finally
        {
            ApplyPhase(targetPhase);
            isFlipping = false;
        }
    }

    private async UniTask FlipOnceAsync(DayNightManager.Phase phase)
    {
        float duration = Mathf.Max(0.01f, flipDuration);
        float elapsed = 0f;
        bool spriteChanged = false;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float ratio = Mathf.Clamp01(elapsed / duration);
            float angle = Mathf.Lerp(0f, 180f, ratio);

            if (!spriteChanged && angle >= 90f)
            {
                ApplyPhase(phase);
                spriteChanged = true;
            }

            float visibleAngle =
                angle <= 90f ? angle : 180f - angle;

            transform.localRotation =
                Quaternion.Euler(0f, visibleAngle, 0f);

            await UniTask.Yield(
                PlayerLoopTiming.Update,
                this.GetCancellationTokenOnDestroy());
        }

        ApplyPhase(phase);
    }

    private void ApplyPhase(DayNightManager.Phase phase)
    {
        isFront = IsFrontPhase(phase);
        coinImage.sprite = isFront ? frontSprite : backSprite;
        transform.localRotation = Quaternion.identity;
    }

    private static bool IsFrontPhase(DayNightManager.Phase phase)
    {
        return phase == DayNightManager.Phase.Day;
    }
}