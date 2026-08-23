using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using NorthLand.Core;

public enum GamePauseReason
{
    Manual,
    Reward,
    Settings,
    ResultDecided,
    Cutscene,
    Tutorial
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
    public bool IsPaused => pauseReasons.Count > 0;

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

    private GameManager gameManager;



    private void Awake()
    {
        if (speedControls == null)
        {
            speedControls = GetComponent<CanvasGroup>();
        }

        if (speedControls == null)
        {
            Debug.LogError("[GameSpeedController] CanvasGroup이 연결되지 않았습니다.",this);
        }

        if (Instance != null && Instance != this)
        {
            Debug.LogError("[GameSpeedController] 씬에 컨트롤러가 두 개 존재합니다.",this);

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

    private void Start()
    {
        gameManager = GameManager.Instance;

        if (gameManager == null)
        {
            Debug.LogError("[GameSpeedController] GameManager를 찾지 못해 게임 종료 시 시간을 정지할 수 없습니다.",this);
            return;
        }

        gameManager.OnResultDecided += HandleResultDecided;
    }

    public void PauseGame()
    {
        bool pause =!pauseReasons.Contains(GamePauseReason.Manual);

        SetPaused(GamePauseReason.Manual, pause);

        if (pause)
        {
            UpdateSelectedButton(0);
        }
        else
        {
            UpdateSelectedButton(GetSpeedButtonIndex());
        }
    }

    private int GetSpeedButtonIndex()
    {
        if (Mathf.Approximately(CurrentSpeed, VeryFastSpeed))
        {
            return 2;
        }

        if (Mathf.Approximately(CurrentSpeed, FastSpeed))
        {
            return 1;
        }

        return 0;
    }

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

    public void SetPaused(GamePauseReason reason, bool paused)
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

        ApplyTimeScale();
        UpdateSelectedButton(buttonIndex);
    }

    private void ApplyTimeScale()
    {
        Time.timeScale = IsPaused ? 0f : CurrentSpeed;
        UpdateControlsLock(IsPaused);
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
    private void HandleResultDecided(GameResult result)
    {
        if (result == GameResult.Playing)
        {
            return;
        }

        SetPaused(GamePauseReason.ResultDecided, true);
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.OnResultDecided -= HandleResultDecided;
        }

        if (Instance != this)
        {
            return;
        }

        Instance = null;
        Time.timeScale = NormalSpeed;
    }
}