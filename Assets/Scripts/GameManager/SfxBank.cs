using System;
using UnityEngine;

/// <summary>
/// 주인이 없는 공용 효과음의 클립·볼륨을 들고 있는 뱅크. (#361 후속)
///
/// <see cref="AudioManager"/>는 DontDestroyOnLoad라 인스펙터 배선을 가질 수 없고, 씬 쪽 <see cref="SoundCue"/>
/// 계층은 "그 씬에서 무엇을 틀지"를 정하는 자리다. 그런데 버튼 클릭음처럼 **어느 씬에서나 같은 소리**는
/// 씬 큐에 넣으면 씬마다 같은 배선을 반복하게 되고, 새 씬을 만든 사람이 빠뜨리면 에러 없이 조용히 무음이 된다
/// (WL-180이 BGM에서 실제로 낸 사고와 같은 형태).
///
/// 그래서 이 소리들만 SO 하나가 소유하고 <see cref="Sfx"/>가 <c>Resources.Load</c>로 1회 집어온다.
/// 재생은 여전히 <see cref="AudioManager"/>가 한다 — 이 SO는 "무엇을 얼마나 크게"만 안다.
///
/// ⚠ **타워 발사음처럼 클립 주인이 이미 있는 소리는 여기 넣지 않는다.** 그쪽은 각자의 SO가 클립을 들고
/// 재생 시 직접 넘기는 방향이다(`Docs/Core/AudioManager.md` §7). 이 뱅크의 범위는 **주인이 없는 공용 소리**다.
///
/// ⚠ 클립은 `Assets/Imported`(별도 저장소)에 있다 — 미동기화 상태에서는 에러 없이 소리만 사라진다(§4.5).
/// </summary>
[CreateAssetMenu(fileName = "SfxBank", menuName = "NorthLand/Audio/SFX Bank")]
public class SfxBank : ScriptableObject
{
    /// <summary>
    /// 클립 하나와 그 클립의 재생 배율. **struct가 아니라 class인 것이 의도다** —
    /// struct는 필드 초기화식을 가질 수 없어 새 항목의 <see cref="Volume"/>이 0으로 태어나고,
    /// 그러면 클립을 꽂아도 소리가 안 나서 "배선했는데 무음"으로 시간을 버린다.
    ///
    /// ⚠ 아래 필드들이 <c>= new Cue()</c>로 초기화된 것도 같은 이유다. **null인 채 두면 초기화식이
    /// 소용없다** — Unity 직렬화기는 null 클래스 필드를 기본값(Volume 0)으로 써 넣고, 역직렬화가
    /// 그 0으로 초기화식 결과를 덮는다. 항목을 늘릴 때 이 관용구를 빠뜨리지 말 것.
    /// </summary>
    [Serializable]
    public class Cue
    {
        [Tooltip("재생할 클립. 비워두면 그 소리는 조용히 생략된다(씬이 깨지지 않는다).")]
        public AudioClip Clip;

        [Range(0f, 1f)]
        [Tooltip("재생 배율. SFX 채널 볼륨에 곱해진다. 임포트 설정에는 클립별 게인이 없어(AudioManager.md §4.5) " +
                 "클립 사이의 레벨 차는 여기서만 맞출 수 있다.")]
        public float Volume = 1f;

        /// <summary>
        /// 이 소리가 마지막으로 재생된 프레임. 같은 프레임의 중복 요청을 걸러내는 데만 쓴다(§Sfx.Play).
        /// 직렬화 대상이 아니다 — 런타임 상태이고 에셋을 더럽히지 않는다.
        /// </summary>
        [NonSerialized] public int LastPlayedFrame = -1;
    }

    [Header("UI")]
    [Tooltip("버튼·토글을 누른 순간. UiClickSfx가 전역으로 재생하므로 버튼마다 배선하지 않는다.")]
    [SerializeField] Cue buttonClick = new Cue();

    [Tooltip("건물·타워를 클릭해 정보 패널이 열리는 순간. 패널이 켜질 때가 아니라 **클릭할 때** 울린다.")]
    [SerializeField] Cue panelOpen = new Cue();

    [Header("피드백")]
    [Tooltip("타워가 실제로 설치된 순간. 일반 배치와 합성 결과 배치가 같은 경로를 쓰므로 둘 다 여기서 울린다.")]
    [SerializeField] Cue towerInstall = new Cue();

    [Tooltip("조작이 반려된 순간 — 배치 불가 지점 클릭, 합성 재료·코스트 부족, 주민 증가 실패 공용. " +
             "지금은 클립 하나를 셋이 공유한다. 소리를 나누고 싶어지면 항목을 쪼개고 Sfx의 메서드도 함께 가른다.")]
    [SerializeField] Cue rejected = new Cue();

    [Tooltip("건물 레벨이 오른 순간. 생산 라인 업그레이드와 업그레이드 전용 건물(본진·마법 연구소)이 공유한다 " +
             "— 둘 다 ManagementController.BuildingAction.Upgraded 한 종류이기 때문이다.")]
    [SerializeField] Cue buildingUpgrade = new Cue();

    [Tooltip("본진에서 주민 수가 늘어난 순간. **전용 소스로 재생된다** — 연타 시 겹치지 않고 처음부터 다시 난다.")]
    [SerializeField] Cue residentIncreased = new Cue();

    [Header("되돌리기")]
    [Tooltip("조작을 되돌린 순간. 되돌리기 버튼과 Ctrl+Z가 같은 소리를 쓴다(UndoRequest 한 곳에서 난다).")]
    [SerializeField] Cue undo = new Cue();

    [Tooltip("다시 실행한 순간. **아직 기능이 없다** — 클립만 미리 꽂아두고 호출부는 다시 실행이 생길 때 잇는다.")]
    [SerializeField] Cue redo = new Cue();

    public Cue ButtonClick => buttonClick;

    public Cue PanelOpen => panelOpen;

    public Cue TowerInstall => towerInstall;

    public Cue Rejected => rejected;

    public Cue BuildingUpgrade => buildingUpgrade;

    public Cue ResidentIncreased => residentIncreased;

    public Cue Undo => undo;

    public Cue Redo => redo;
}
