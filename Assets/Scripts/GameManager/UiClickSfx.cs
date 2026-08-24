using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 눌린 것이 버튼·토글이면 클릭음을 낸다. **버튼마다 배선하지 않는다.**
///
/// ── 왜 버튼별 리스너가 아닌가 ────────────────────────────────────────
///
/// <c>onClick.AddListener</c>를 쓰는 파일이 이미 30개고, 상점 교환 행·보상 카드·타워 팔레트처럼
/// **런타임에 생성되는 버튼**이 그중 상당수다. 하나씩 붙이는 방식은 "새 버튼을 만든 사람이 잊으면
/// 그 버튼만 조용히 무음"이 되는데, 그건 리뷰로도 컴파일러로도 잡히지 않는다.
///
/// 그래서 입력 쪽에 훅을 하나 걸고 눌린 대상을 역으로 찾는다. 배선이 0이라 잊을 수가 없다.
///
/// ── 판정 방법 ────────────────────────────────────────────────────
///
/// 좌클릭이 눌린 프레임에 <see cref="EventSystem"/> 레이캐스트를 한 번 돌리고, **최상단 히트에서
/// 부모로 올라가며** 첫 <see cref="Selectable"/>을 찾는다. 이건 흉내가 아니라 EventSystem이 실제로
/// 누를 대상을 고르는 규칙과 같은 순서다(<c>ExecuteHierarchy</c>) — 그래서 "소리는 났는데 버튼은
/// 안 눌렸다"가 생기지 않는다. 모달 배경처럼 위를 덮은 그래픽이 있으면 그쪽이 최상단이 되어
/// 버튼을 못 찾고, 그때는 실제로도 버튼이 안 눌리므로 결과가 일치한다.
///
/// **누르는 순간**에 낸다(뗄 때가 아니라). 버튼을 누른 채 밖으로 끌어 취소해도 소리는 이미 난 셈인데,
/// 조작감에서는 반응이 빠른 쪽이 낫다고 보고 그 대가를 받아들였다.
///
/// 자기 소리를 따로 내는 버튼은 <see cref="UiClickSfxIgnore"/>를 붙여 뺀다.
///
/// 부팅은 <see cref="AudioManager"/>와 같은 패턴이다(씬 배치 없음 — 모든 씬에서 필요하고, 씬에 두면
/// 씬 파일 병합 충돌만 늘어난다). 씬 탐색을 하지 않으므로 상주 비용은 <c>Update</c>의 버튼 상태 조회뿐이다.
/// </summary>
public class UiClickSfx : MonoBehaviour
{
    private static UiClickSfx instance;

    // 매 클릭마다 새로 만들면 클릭 수만큼 GC 쓰레기가 난다 — 재사용한다.
    private readonly List<RaycastResult> results = new List<RaycastResult>();

    private PointerEventData pointerData;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        var go = new GameObject(nameof(UiClickSfx));

        go.AddComponent<UiClickSfx>();

        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        // 마우스가 없는 환경(패드·터치 전용)에서는 조용히 아무것도 하지 않는다.
        Mouse mouse = Mouse.current;

        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        EventSystem eventSystem = EventSystem.current;

        if (eventSystem == null)
        {
            return;
        }

        // `IsPointerOverGameObject()`로 먼저 걸러내지 않는다 — 그건 **입력 모듈이 이번 프레임에 이미
        // 돌았는지**에 따라 직전 프레임의 답을 주는데, 스크립트 실행 순서상 그게 보장되지 않는다.
        // 아래 레이캐스트가 같은 판정을 순서 의존 없이 내주므로, 한 번 더 물어보는 것은 이득 없이
        // "가끔 클릭음만 안 나는" 재현 어려운 결함만 만든다. 레이캐스트는 클릭한 프레임에만 돈다.
        Selectable pressed = PickSelectable(eventSystem, mouse.position.ReadValue());

        if (pressed == null || !pressed.IsInteractable())
        {
            return;
        }

        // 버튼·토글만 낸다. 슬라이더·스크롤바는 "누르는" 것이 아니라 끄는 조작이라 클릭음이 어울리지 않는다.
        if (!(pressed is Button) && !(pressed is Toggle))
        {
            return;
        }

        // 자기 소리를 따로 내는 버튼은 뺀다. 부모까지 보므로 패널 단위로 한 번에 끌 수도 있다.
        if (pressed.GetComponentInParent<UiClickSfxIgnore>() != null)
        {
            return;
        }

        Sfx.ButtonClick();
    }

    private Selectable PickSelectable(EventSystem eventSystem, Vector2 screenPosition)
    {
        pointerData ??= new PointerEventData(eventSystem);

        pointerData.Reset();
        pointerData.position = screenPosition;

        results.Clear();
        eventSystem.RaycastAll(pointerData, results);

        if (results.Count == 0)
        {
            return null;
        }

        // results[0]이 최상단이다. 거기서 부모로 올라가며 찾는 것이 EventSystem의 대상 선정과 같은 규칙.
        return results[0].gameObject != null
            ? results[0].gameObject.GetComponentInParent<Selectable>()
            : null;
    }
}
