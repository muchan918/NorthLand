using System;
using UnityEngine;

/// <summary>
/// 커서 그림의 종류 = <b>지금 무엇을 가리키고 있는가 / 무엇을 하는 중인가</b>.
/// 호버 대상이 <see cref="ICursorHint"/>로 답할 때, 그리고 <see cref="CursorSet"/>이 텍스처를 꽂는
/// 칸 이름으로 쓰이는 공용 어휘다.
///
/// 이름이 그림(문·돋보기·손)이 아니라 <b>대상</b>(건물·타워·주민) 기준인 것은 의도다 — 아트 파일명
/// (<c>Building-Hover-32</c> 등)과 1:1로 맞아 "어느 그림을 어느 칸에 꽂는지"가 바로 보이고, 나중에
/// 건물 커서를 문에서 다른 그림으로 바꿔도 이름이 거짓말이 되지 않는다.
///
/// ⚠ <b>이름에 도메인이 들어와도 원칙 2는 유지된다</b>(<c>MouseManager.md</c> §1). 이 어휘를 고르는 것은
/// <b>대상 자신</b>이고(<see cref="ICursorHint"/>), <see cref="CursorController"/>는 여전히
/// "타워인가 주민인가"를 묻지 않는다 — 컨트롤러가 분기하는 것은 아래 <b>모드 3종</b>과
/// <b>「대상 표면 위인가」</b>(<c>MouseManager.IsOverTargetSurface</c>)뿐이다.
///
/// ⚠ <b>"눌림"은 여기 없다.</b> 좌버튼 눌림은 종류가 아니라 <b>각 종류의 변형</b>이다
/// (<see cref="CursorSet.Entry.Pressed"/>) — "건물 위에서 누르면 문이 열린다"처럼 눌렸을 때의 그림이
/// 무엇을 가리키느냐에 따라 다르기 때문이다. 전역 눌림 그림 1장으로는 표현할 수 없다.
///
/// ⚠ 종류를 늘릴 때는 <see cref="CursorSet"/>에 필드 하나와 <see cref="CursorSet.TryGet"/>의
/// switch 한 줄을 함께 추가한다. 하나라도 빠지면 컴파일 에러로 잡힌다(그게 배열+enum 대신
/// 이름 있는 필드를 쓰는 이유다 — <c>SfxBank</c>가 같은 판단을 했다).
/// </summary>
public enum CursorKind
{
    /// <summary>아무것도 가리키지 않을 때. 칸이 비어 있으면 OS 기본 커서로 돌아간다.</summary>
    Default,

    /// <summary>
    /// 누를 수 있는 UI 위(버튼·토글 등 <c>Selectable</c>, <b>비활성 상태는 제외</b>).
    ///
    /// ⚠ <b>"UI 위"가 아니라 "누를 수 있는 UI 위"다.</b> 패널 배경이나 라벨처럼 반응하지 않는 UI에서는
    /// <see cref="Default"/> 그대로다 — UI에 올라갔다고 무조건 커서가 바뀌면 오히려 헷갈린다는
    /// 판단이라(그 버전은 넣었다가 걷어냈다), 바뀌는 것 자체가 "여기는 누를 수 있다"는 신호가 된다.
    /// </summary>
    UIButton,

    /// <summary>우버튼을 쥐고 화면을 끌어 옮기는 동안(카메라 팬).</summary>
    CameraPan,

    // ── 호버 대상별 (대상이 ICursorHint로 지정) ─────────────────────
    /// <summary>건물 위.</summary>
    Building,

    /// <summary>타워 위.</summary>
    Tower,

    /// <summary>주민 위.</summary>
    Resident,

    // ── 상호작용 모드별 (컨트롤러가 모드로 판정) ────────────────────
    /// <summary>주민을 실제로 집어 끌고 있는 동안.</summary>
    ResidentDrag,

    /// <summary>배치 고스트를 들고 있는 동안.</summary>
    Placing,

    /// <summary>스킬 조준 중.</summary>
    SkillAiming,

    /// <summary>
    /// <b>지금 이 자리에서는 할 수 없다.</b> 배치·조준 중 커서가 대상 표면(타일) 밖일 때
    /// (<c>MouseManager.IsOverTargetSurface == false</c>) 컨트롤러가 고른다.
    ///
    /// <b>배치와 조준이 칸을 나눠 갖지 않는 이유</b>: 알리는 내용이 같다 — "여기엔 못 놓는다"와
    /// "여기엔 못 쓴다"를 다른 그림으로 구분해야 할 이유가 아직 없다. 갈라야 하면 칸을 하나 더 판다.
    ///
    /// ⚠ <b>UI 위에서는 이 종류가 나오지 않는다.</b> "여기엔 못 놓는다"는 <b>지도</b>에 대한 말이라
    /// 패널 위에서는 거짓말이 된다(<see cref="CursorController"/>의 판정 참고).
    /// </summary>
    Blocked,
}

/// <summary>
/// 상태별 커서 텍스처와 핫스팟을 들고 있는 뱅크. <see cref="CursorController"/>가
/// <c>Resources.Load</c>로 1회 집어온다.
///
/// 인스펙터 배선이 아니라 SO인 이유는 <c>SfxBank</c>와 같다 — 소비자인 컨트롤러가 씬에 놓이지 않고
/// 런타임에 스스로 부팅하므로(씬 파일을 안 건드리려고), 인스펙터에 에셋을 꽂을 자리가 아예 없다.
///
/// ⚠ 텍스처는 <b>Texture Type = Cursor</b>로 임포트해야 한다. <c>Cursor.SetCursor</c>는 그리는 주체와
/// 무관하게 픽셀을 CPU에서 읽으므로, 압축돼 있거나 Read/Write가 꺼져 있으면
/// <c>"Failed to set the cursor because the specified texture was not CPU accessible"</c>로 실패하고
/// 기본 화살표로 폴백한다. 컨트롤러가 적용 전에 이 조건을 검사해 경고한다.
/// </summary>
[CreateAssetMenu(fileName = "CursorSet", menuName = "NorthLand/Input/Cursor Set")]
public class CursorSet : ScriptableObject
{
    /// <summary>그림 한 장과 그 그림의 핫스팟.</summary>
    [Serializable]
    public struct Art
    {
        [Tooltip("커서 그림. Texture Type = Cursor 로 임포트할 것.")]
        public Texture2D Texture;

        [Tooltip("이 그림 안에서 '실제로 가리키는 점'의 픽셀 좌표(좌상단 기준). " +
                 "⚠ 그림마다 크기가 다르면 좌표도 각각 다시 잰다 — 32용 값을 70에 그대로 쓰면 어긋난다.")]
        public Vector2 Hotspot;
    }

    /// <summary>
    /// 한 종류의 커서. 평상시 그림과 <b>좌버튼을 누르고 있는 동안</b>의 그림을 함께 갖는다.
    ///
    /// 눌림 칸이 비면 평상시 그림을 그대로 쓴다 — <b>즉 그 종류는 눌림 연출이 없다</b>는 뜻이고,
    /// 오류가 아니다. 건물처럼 한 쌍이 있는 것만 채우면 된다.
    /// </summary>
    [Serializable]
    public struct Entry
    {
        [Tooltip("이 상태에서 커서를 아예 숨긴다. 켜면 아래 그림 두 칸은 무시된다. " +
                 "고스트나 조준 인디케이터가 커서 자리를 대신하는 상태에 쓴다.")]
        public bool Hidden;

        public Art Normal;

        [Tooltip("좌버튼을 누르고 있는 동안의 그림. 비우면 Normal을 그대로 쓴다(눌림 연출 없음).")]
        public Art Pressed;
    }

    [Header("그리기 방식")]
    [Tooltip("Auto = OS가 그리는 하드웨어 커서(지연 0, 크기는 시스템 커서 크기로 합성됨). " +
             "ForceSoftware = Unity가 직접 그림(크기 제한 없음, 1프레임 지연).")]
    [SerializeField] CursorMode _cursorMode = CursorMode.Auto;

    [Header("기본")]
    [SerializeField] Entry _default;

    [Tooltip("누를 수 있는 UI(버튼·토글 등) 위. 반응하지 않는 UI에서는 Default 그대로다.")]
    [SerializeField] Entry _uiButton;

    [Tooltip("우버튼을 쥐고 화면을 끌어 옮기는 동안(카메라 팬).")]
    [SerializeField] Entry _cameraPan;


    [Header("호버 대상별 — 대상이 ICursorHint로 지정")]
    [SerializeField] Entry _building;
    [SerializeField] Entry _tower;
    [SerializeField] Entry _resident;

    [Header("상호작용 모드별 — 컨트롤러가 모드로 판정")]
    [SerializeField] Entry _residentDrag;

    [Tooltip("배치 고스트를 들고 대상 표면(타일) 위에 있는 동안. Hidden을 켜 고스트에 자리를 내준다.")]
    [SerializeField] Entry _placing;

    [Tooltip("스킬 조준 중 전투 타일 위. Hidden을 켜 범위 인디케이터에 자리를 내준다.")]
    [SerializeField] Entry _skillAiming;

    [Tooltip("배치·조준 중 대상 표면 밖 — '여기서는 안 된다'. UI 위에서는 나오지 않는다.")]
    [SerializeField] Entry _blocked;

    /// <summary>
    /// 커서를 누가 그리는가. <b>크기 제한은 여기서 갈린다.</b>
    ///
    /// <see cref="CursorMode.Auto"/>는 OS에 비트맵을 넘겨 OS가 그린다 — 지연이 0인 대신 그림이
    /// <b>시스템 커서 크기로 합성된다</b>(Windows 기본 32×32, DPI·접근성 설정에 따라 커짐).
    /// 그보다 큰 그림은 축소되며, 배수가 딱 떨어지지 않으면 픽셀아트가 뭉갠다.
    ///
    /// <see cref="CursorMode.ForceSoftware"/>는 Unity가 직접 그린다 — OS 크기 제한이 사라져
    /// 70×70 같은 그림도 원본 크기로 나오지만, 커서가 <b>한 프레임 뒤처진다</b>.
    /// </summary>
    public CursorMode CursorMode => _cursorMode;

    /// <summary>이 종류에서 커서를 숨기는가(고스트·인디케이터가 커서를 대신하는 상태).</summary>
    public bool IsHidden(CursorKind kind) => Pick(kind).Hidden;

    /// <summary>
    /// 해당 종류의 그림을 꺼낸다. <paramref name="pressed"/>가 참이면 눌림 그림을 우선하고,
    /// 그 칸이 비어 있으면 평상시 그림으로 내려간다. 둘 다 비면 <c>false</c> —
    /// 그때의 폴백(기본 칸 → OS 기본 커서)은 호출부가 결정한다.
    /// </summary>
    public bool TryGet(CursorKind kind, bool pressed, out Texture2D texture, out Vector2 hotspot)
    {
        Entry entry = Pick(kind);
        Art art = pressed && entry.Pressed.Texture != null ? entry.Pressed : entry.Normal;

        texture = art.Texture;
        hotspot = art.Hotspot;
        return texture != null;
    }

    private Entry Pick(CursorKind kind) => kind switch
    {
        CursorKind.UIButton => _uiButton,
        CursorKind.CameraPan => _cameraPan,
        CursorKind.Building => _building,
        CursorKind.Tower => _tower,
        CursorKind.Resident => _resident,
        CursorKind.ResidentDrag => _residentDrag,
        CursorKind.Placing => _placing,
        CursorKind.SkillAiming => _skillAiming,
        CursorKind.Blocked => _blocked,
        _ => _default,
    };
}
