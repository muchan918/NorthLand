using TMPro;
using UnityEngine;
using NorthLand.Core;

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
            seedGamePanle.SetActive(true);
        }

        public void OnClickCloseseedGamePanle()
        {
            seedGamePanle.SetActive(false);
        }



    }
}