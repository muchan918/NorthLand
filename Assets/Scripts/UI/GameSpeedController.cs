using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public enum GamePauseReason
{
    Reward,
    Settings,
    GameOver,
    Cutscene
}

public class GameSpeedController : MonoBehaviour
{
    private const float NormalSpeed = 1f;
    private const float FastSpeed = 2f;
    private const float VeryFastSpeed = 4f;

    [SerializeField]
    private Button[] speedButtons;

    [SerializeField]
    private Color selectedButtonColor = Color.gray;

    public float CurrentSpeed { get; private set; } = NormalSpeed;
    //public bool IsPaused { get; private set; }

    private readonly HashSet<GamePauseReason> pauseReasons = new();
    private ColorBlock[] defaultButtonColors;
    public static GameSpeedController Instance { get; private set; }

    [SerializeField]
    private CanvasGroup speedControls;

    [SerializeField]
    [Range(0f, 1f)]
    private float pausedControlsAlpha = 0.8f;

    private bool controlsLocked;
    private bool interactableBeforePause;
    private float alphaBeforePause;


    private void Awake()
    {
        if (speedControls == null)
        {
            speedControls = GetComponent<CanvasGroup>();
        }

        if (speedControls == null)
        {
            Debug.LogError(
                "[GameSpeedController] CanvasGroup이 연결되지 않았습니다.",
                this);
        }

        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                "[GameSpeedController] 씬에 컨트롤러가 두 개 존재합니다.",
                this);

            return;
        }

        Instance = this;

        if (speedButtons == null)
        {
            speedButtons = new Button[0];
        }

        defaultButtonColors = new ColorBlock[speedButtons.Length];

        for (int i = 0; i < speedButtons.Length; i++)
        {
            if (speedButtons[i] != null)
            {
                defaultButtonColors[i] = speedButtons[i].colors;
            }
        }

        SetSpeed(NormalSpeed, 0);
    }


    //일시 정지 일단은 구현만 해두고 사용할지 안할지는 나중에 결정
    //public void PauseGame()
    //{
    //    IsPaused = !IsPaused;
    //    ApplyTimeScale();

    //    if (IsPaused)
    //    {
    //        UpdateSelectedButton(0);
    //    }
    //    else
    //    {
    //        UpdateSelectedButton(GetSpeedButtonIndex());
    //    }
    //}

    //private int GetSpeedButtonIndex()
    //{
    //    if (Mathf.Approximately(CurrentSpeed, VeryFastSpeed))
    //    {
    //        return 3;
    //    }

    //    if (Mathf.Approximately(CurrentSpeed, FastSpeed))
    //    {
    //        return 2;
    //    }

    //    return 1;
    //}

    public void SetNormalSpeed()
    {
        SetSpeed(NormalSpeed, 0);
    }

    public void SetFastSpeed()
    {
        SetSpeed(FastSpeed, 1);
    }

    public void SetVeryFastSpeed()
    {
        SetSpeed(VeryFastSpeed, 2);
    }

    public void SetPaused(GamePauseReason reason,bool paused)
    {
        bool changed;

        if (paused)
        {
            changed = pauseReasons.Add(reason);
        }
        else
        {
            changed = pauseReasons.Remove(reason);
        }

        if (changed)
        {
            ApplyTimeScale();
        }
    }

    private void SetSpeed(float speed, int buttonIndex)
    {
        CurrentSpeed = speed;
        //IsPaused = false;

        ApplyTimeScale();
        UpdateSelectedButton(buttonIndex);
    }

    private void ApplyTimeScale()
    {
        bool paused = pauseReasons.Count > 0;

        Time.timeScale = paused? 0f: CurrentSpeed;

        UpdateControlsLock(paused);
    }

    private void UpdateControlsLock(bool paused)
    {
        if (speedControls == null)
        {
            return;
        }

        if (paused && !controlsLocked)
        {
            interactableBeforePause =speedControls.interactable;

            alphaBeforePause =speedControls.alpha;

            speedControls.interactable = false;
            speedControls.alpha = pausedControlsAlpha;
            controlsLocked = true;
        }
        else if (!paused && controlsLocked)
        {
            speedControls.interactable =interactableBeforePause;

            speedControls.alpha =alphaBeforePause;

            controlsLocked = false;
        }
    }


    private void UpdateSelectedButton(int selectedIndex)
    {
        for (int i = 0; i < speedButtons.Length; i++)
        {
            Button button = speedButtons[i];

            if (button == null)
            {
                continue;
            }

            ColorBlock colors = defaultButtonColors[i];

            if (i == selectedIndex)
            {
                colors.normalColor = selectedButtonColor;
                colors.highlightedColor = selectedButtonColor;
                colors.selectedColor = selectedButtonColor;
            }

            button.colors = colors;
        }
    }


    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        Instance = null;
        Time.timeScale = NormalSpeed;
    }
}