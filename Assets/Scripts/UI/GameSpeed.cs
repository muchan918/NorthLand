using UnityEngine;
using UnityEngine.UI;

public class GameSpeed : MonoBehaviour
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

    private bool isRewardPaused;
    private ColorBlock[] defaultButtonColors;

    private void Awake()
    {
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

    public void SetRewardPaused(bool paused)
    {
        isRewardPaused = paused;
        ApplyTimeScale();
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
        Time.timeScale = isRewardPaused? 0f: CurrentSpeed;
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
        Time.timeScale = NormalSpeed;
    }
}