using UnityEngine;

/// <summary>
/// 튜토리얼 실행 중에는 이 GameObject를 숨긴다.
/// TutorialMode는 씬 로드 전에 확정되고 튜토리얼 종료 시 씬을 다시 로드하므로,
/// Awake에서 한 번 적용하는 것으로 충분하다.
/// </summary>
public class HideDuringTutorial : MonoBehaviour
{
    private void Awake()
    {
        gameObject.SetActive(!TutorialMode.IsActive);
    }
}
