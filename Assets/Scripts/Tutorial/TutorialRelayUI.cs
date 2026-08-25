using Cysharp.Threading.Tasks;
using NorthLand.Core;
using System;
using UnityEngine;
using UnityEngine.UI;

public class TutorialRelayUI : MonoBehaviour
{
    [SerializeField]
    private Button tutorialButton;

    [SerializeField]
    private GameObject popupRoot;

    [SerializeField]
    private Button confirmButton;

    [SerializeField]
    private Button cancelButton;

    [SerializeField]
    private RunSaveManager runSaveManager;

    private bool _pausedByPopup;

    private bool isDeletingRun;

    private void Awake()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        tutorialButton.onClick.AddListener(OpenPopup);
        confirmButton.onClick.AddListener(ConfirmReplay);
        cancelButton.onClick.AddListener(ClosePopup);

        popupRoot.SetActive(false);
        tutorialButton.gameObject.SetActive(!TutorialMode.IsActive);
    }

    private void OnDestroy()
    {
        if (tutorialButton != null)
        {
            tutorialButton.onClick.RemoveListener(OpenPopup);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(ConfirmReplay);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(ClosePopup);
        }

        ResumeGameIfPaused();
    }

    private bool ValidateReferences()
    {
        bool valid = true;

        if (tutorialButton == null) { LogMissing(nameof(tutorialButton)); valid = false; }
        if (popupRoot == null) { LogMissing(nameof(popupRoot)); valid = false; }
        if (confirmButton == null) { LogMissing(nameof(confirmButton)); valid = false; }
        if (cancelButton == null) { LogMissing(nameof(cancelButton)); valid = false; }
        if (runSaveManager == null) { LogMissing(nameof(runSaveManager)); valid = false; }

        return valid;
    }

    private void LogMissing(string fieldName)
    {
        Debug.LogError($"[{nameof(TutorialRelayUI)}] {fieldName}이(가) 연결되지 않았습니다.", this);
    }

    private void OpenPopup()
    {
        MouseManager.Instance?.CancelInteractions();
        popupRoot.SetActive(true);

        if (!_pausedByPopup && GameSpeedController.Instance != null)
        {
            GameSpeedController.Instance.SetPaused(GamePauseReason.Tutorial, true);
            _pausedByPopup = true;
        }
    }

    private void ClosePopup()
    {
        popupRoot.SetActive(false);
        ResumeGameIfPaused();
    }

    private void ConfirmReplay()
    {
        ConfirmReplayAsync().Forget();
    }
    private async UniTaskVoid ConfirmReplayAsync()
    {
        if (isDeletingRun || runSaveManager == null)
        {
            return;
        }

        isDeletingRun = true;
        confirmButton.interactable = false;

        try
        {
            SaveResult result = await runSaveManager.DeleteCurrentRunAsync(this.GetCancellationTokenOnDestroy());

            if (!result.Success)
            {
                Debug.LogError($"[{nameof(TutorialRelayUI)}] 기존 Run을 초기화하지 못했습니다: {result.Error}",this);

                return;
            }

            ClosePopup();

            GameSceneManager sceneManager = GameSceneManager.Instance;

            if (sceneManager == null)
            {
                Debug.LogError($"[{nameof(TutorialRelayUI)}] GameSceneManager 인스턴스를 찾을 수 없습니다.",this);

                return;
            }

            sceneManager.LoadTutorial();
        }
        catch (OperationCanceledException)
        {
            // UI 파괴에 따른 정상 취소
        }
        finally
        {
            isDeletingRun = false;

            if (confirmButton != null)
            {
                confirmButton.interactable = true;
            }
        }
    }


    private void ResumeGameIfPaused()
    {
        if (!_pausedByPopup)
        {
            return;
        }

        _pausedByPopup = false;
        GameSpeedController.Instance?.SetPaused(GamePauseReason.Tutorial, false);
    }
}
