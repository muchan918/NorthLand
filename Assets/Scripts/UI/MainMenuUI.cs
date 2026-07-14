using UnityEngine;
using NorthLand.Core;

namespace NorthLand.UI
{
    // 메인 메뉴 화면의 버튼 동작을 담당하는 컨트롤러.
    // 씬 전환 자체는 GameSceneManager에 위임하고, 이 스크립트는 버튼 입력을
    // 매니저 호출로 이어주는 역할만 한다. (설정/종료 버튼은 이후 메서드 추가로 확장)
    public class MainMenuUI : MonoBehaviour
    {
        // "게임 시작" 버튼의 OnClick에 연결한다. 경영 공간으로 전환한다.
        public void OnClickStart()
        {
            if (GameSceneManager.Instance == null)
            {
                Debug.LogError("[MainMenuUI] GameSceneManager 인스턴스가 없습니다. 씬 전환 불가.");
                return;
            }

            GameSceneManager.Instance.LoadManageSpace();
        }
    }
}
