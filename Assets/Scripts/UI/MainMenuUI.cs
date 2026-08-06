using TMPro;
using UnityEngine;
using NorthLand.Core;
using UnityEngine.UI;

namespace NorthLand.UI
{
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Seed")]

        [SerializeField]
        private TMP_InputField seedInputField;

        [SerializeField]
        private TMP_Text seedErrorText;


        [SerializeField]
        private GameObject seedGamePanle;

        [Header("Continue")]

        [SerializeField]
        private Button continueButton;

        private void Awake()
        {
            RefreshContinueButton();
        }

        // 랜덤 시드로 시작
        public void OnClickStart()
        {
            if (!TryGetSceneManager(out GameSceneManager sceneManager))
            {
                return;
            }

            sceneManager.LoadManageSpace();
        }

        // 플레이어가 입력한 시드로 시작
        public void OnClickStartWithSeed()
        {
            ClearSeedError();

            if (!TryGetSceneManager(out GameSceneManager sceneManager))
            {
                return;
            }

            if (seedInputField == null)
            {
                ShowSeedError("시드 입력창이 연결되지 않았습니다.");

                return;
            }

            string input = seedInputField.text.Trim();

            if (!int.TryParse(input,out int masterSeed) ||masterSeed <= 0)
            {
                ShowSeedError("1~2147483647 사이의 숫자를 입력하세요.");

                return;
            }

            sceneManager.LoadManageSpaceWithSeed(masterSeed);
        }

        private bool TryGetSceneManager(out GameSceneManager sceneManager)
        {
            sceneManager = GameSceneManager.Instance;

            if (sceneManager != null)
            {
                return true;
            }

            Debug.LogError("[MainMenuUI] GameSceneManager 인스턴스가 없습니다.");

            ShowSeedError("게임을 시작할 수 없습니다.");

            return false;
        }

        private void ShowSeedError(string message)
        {
            if (seedErrorText != null)
            {
                seedErrorText.text = message;
            }

            Debug.LogWarning($"[MainMenuUI] {message}",this);
        }

        private void ClearSeedError()
        {
            if (seedErrorText != null)
            {
                seedErrorText.text = string.Empty;
            }
        }

        public void OnClickOpenseedGamePanle()
        {
            SetSeedGamePanelActive(true);
        }

        public void OnClickCloseseedGamePanle()
        {
            SetSeedGamePanelActive(false);
        }

        private void SetSeedGamePanelActive(bool isActive)
        {
            if (seedGamePanle == null)
            {
                Debug.LogError("[MainMenuUI] 시드 게임 패널이 연결되지 않았습니다.",this);

                return;
            }

            seedGamePanle.SetActive(isActive);
        }

        /// <summary>
        /// 정상적으로 읽을 수 있는 세이브가 있을 때만 이어하기 버튼을 표시한다.
        /// </summary>
        private void RefreshContinueButton()
        {
            if (continueButton == null)
            {
                Debug.LogError(
                    "[MainMenuUI] 이어하기 버튼이 연결되지 않았습니다.",
                    this);

                return;
            }

            bool canContinue =
                HasLoadableSave(out string error);

            continueButton.gameObject.SetActive(canContinue);

            if (!canContinue &&
                !string.IsNullOrEmpty(error))
            {
                Debug.LogWarning(
                    $"[MainMenuUI] 이어하기 숨김: {error}",
                    this);
            }
        }

        private bool HasLoadableSave(out string error)
        {
            var fileStore = new SaveFileStore(Application.persistentDataPath);

            if (!fileStore.Exists)
            {
                error = null;
                return false;
            }

            if (!fileStore.TryRead(out string json,out error))
            {
                return false;
            }

            var serializer = new SaveSerializer();

            if (!serializer.TryDeserialize(json,out RunData data,out error))
            {
                return false;
            }

            if (data == null)
            {
                error = "세이브 RunData가 비어 있습니다.";
                return false;
            }

            error = null;
            return true;
        }

        public void OnClickContinue()
        {
            // 타이틀이 열린 뒤 파일이 삭제·손상됐을 가능성도 다시 확인한다.
            if (!HasLoadableSave(out string error))
            {
                if (continueButton != null)
                    continueButton.gameObject.SetActive(false);

                Debug.LogWarning($"[MainMenuUI] 이어하기를 시작할 수 없습니다: {error}",this);

                return;
            }

            if (!TryGetSceneManager(out GameSceneManager sceneManager))
            {
                return;
            }

            sceneManager.LoadContinue();
        }
    }
}