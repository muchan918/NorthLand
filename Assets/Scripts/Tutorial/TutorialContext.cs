using UnityEngine;

// 조건이 필요로 하는 씬 참조를 모아 넘겨준다.
// 조건마다 FindFirstObjectByType을 부르지 않게 하려는 것이 목적이다.
// 새 시스템의 조건이 생기면 여기에 프로퍼티를 하나 추가한다.
public class TutorialContext
{
    private ManagementController _management;

    // ManagementController에는 static Instance가 없다(DayNightManager와 다르다).
    // 씬에 하나뿐이므로 처음 요청될 때 찾아서 캐시한다.
    public ManagementController Management
    {
        get
        {
            if (_management == null)
            {
                _management = Object.FindFirstObjectByType<ManagementController>();
            }

            return _management;
        }
    }

    public DayNightManager DayNight => DayNightManager.Instance;
}