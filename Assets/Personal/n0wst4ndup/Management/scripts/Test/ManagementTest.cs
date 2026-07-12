using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 자원 시스템(지갑 + 생산처) 통합 테스트 헬퍼. 주민·낮/밤 시스템이 아직 없으므로 그 둘을
/// 키 입력으로 흉내 내어, 생산처의 두 경계 심(주민 수 입력 / 정산 트리거)을 손으로 구동한다.<br/>
/// - 숫자키(1-N): 해당 생산 라인에 주민 +1 배치 (낮에만) — 주민 시스템 심<br/>
/// - Shift + 숫자키(1-N): 해당 생산 라인에서 주민 -1 회수<br/>
/// - 전 라인 배치 합계는 <see cref="maxVillagers"/>(총 보유 주민 수)를 넘을 수 없다.<br/>
/// - Space: 낮/밤 전환. 낮→밤에 자원 정산(생산), 밤→낮에 주민 배치 초기화 — 낮/밤 시스템 심(팀 계약 #5)<br/>
/// 모든 단계는 Debug.Log로 흐름을 남긴다.<br/>
/// <br/>
/// <see cref="AssignVillager"/>/<see cref="UnassignVillager"/>는 나중에 생산 라인 패널의 +/- 버튼이
/// 그대로 호출할 수 있도록 분리해 두었다.<br/>
/// <br/>
/// ※ 개발/테스트 전용 하네스다. 게임플레이 입력 단일 창구 계약(팀 계약 #1)은 게임플레이 코드 대상이며,
///   이 하네스는 흐름 확인을 위해 Keyboard.current를 직접 읽는다. 실제 게임플레이 입력은 MouseManager를 통한다.<br/>
/// <br/>
/// (Docs/ManagementArea/Resources.md — 이슈 #42)
/// </summary>
public class ManagementTest : MonoBehaviour
{
    [Tooltip("생산처로 만들 산출 자원들. ResourceID가 ResourceTable CSV(wood/iron/food/mana 등)와 일치해야 한다.")]
    [SerializeField] private ResourceAsset[] _resourceAssets;

    [Tooltip("주민 1명당 생산량 (모든 생산처 공통, 테스트용)")]
    [SerializeField] private int _baseAmountPerVillager = 5;

    [Tooltip("총 보유(최대) 주민 수. 전 생산 라인 배치 합계가 이 값을 넘을 수 없다. 플레이 중 조정 가능.")]
    public int maxVillagers = 5;

    private static readonly Key[] DigitKeys =
    {
        Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
        Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
    };

    private enum Phase { Day, Night }

    private ResourceWallet _wallet;
    private ResourceProductionSource[] _sources;
    private int[] _villagerCounts;

    private Phase _phase = Phase.Day;
    private int _day = 1;

    private void Start()
    {
        _wallet = new(); // TODO: 나중에 게임플레이 코드에서 싱글톤 혹은 외부 주입으로 처리
        _wallet.OnChanged += (kind, value) => Debug.Log($"[지갑] {kind} = {value}");

        int count = _resourceAssets != null ? _resourceAssets.Length : 0;
        _sources = new ResourceProductionSource[count];
        _villagerCounts = new int[count];

        // ResourceAsset.Data는 호출부가 채우는 규약(SystemMap §2) — DataTable에서 채운다.
        ResourceTable table = DataTableManager.Get<ResourceTable>("ResourceTable");

        for (int i = 0; i < count; i++)
        {
            ResourceAsset asset = _resourceAssets[i];
            if (asset == null)
            {
                Debug.LogError($"[설정] {i}번 ResourceAsset이 비어 있습니다.");
                continue;
            }

            if (asset.Data == null && table != null)
            {
                asset.Data = table.Get(asset.ResourceID);
            }
            if (asset.Data == null)
            {
                Debug.LogError($"[설정] '{asset.ResourceID}' 의 Data를 채우지 못했습니다. 생산처 {i} 비활성.");
                continue;
            }

            _sources[i] = new(asset, _baseAmountPerVillager, _wallet);
            Debug.Log($"[설정] 생산처 {i + 1}번: {asset.Data.DisplayName}({asset.Data.Kind}), 주민당 생산량 {_baseAmountPerVillager}");
        }

        LogHelp();
        LogPhase();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        bool removing = keyboard.shiftKey.isPressed;
        for (int i = 0; i < _sources.Length && i < DigitKeys.Length; i++)
        {
            if (!keyboard[DigitKeys[i]].wasPressedThisFrame)
            {
                continue;
            }

            if (removing)
            {
                UnassignVillager(i);
            }
            else
            {
                AssignVillager(i);
            }
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            TogglePhase();
        }
    }

    // 배치된 총 주민 수 (전 라인 합계).
    private int AssignedTotal()
    {
        int total = 0;
        for (int i = 0; i < _villagerCounts.Length; i++)
        {
            total += _villagerCounts[i];
        }
        return total;
    }

    // 생산 라인에 주민 +1 (주민 시스템 심). 나중에 패널의 + 버튼이 그대로 호출.
    public void AssignVillager(int index)
    {
        if (!CanEditLine(index))
        {
            return;
        }
        if (AssignedTotal() >= maxVillagers)
        {
            Debug.Log($"[주민] 가용 주민이 없습니다. (배치 {AssignedTotal()}/{maxVillagers})");
            return;
        }

        _villagerCounts[index]++;
        LogAssignment(index, "+1 배치");
    }

    // 생산 라인에서 주민 -1 (주민 시스템 심). 나중에 패널의 - 버튼이 그대로 호출.
    public void UnassignVillager(int index)
    {
        if (!CanEditLine(index))
        {
            return;
        }
        if (_villagerCounts[index] <= 0)
        {
            Debug.Log($"[주민] {index + 1}번 라인에 회수할 주민이 없습니다.");
            return;
        }

        _villagerCounts[index]--;
        LogAssignment(index, "-1 회수");
    }

    private bool CanEditLine(int index)
    {
        if (_phase != Phase.Day)
        {
            Debug.Log("[주민] 밤에는 배치를 변경할 수 없습니다. (Space로 낮 전환)");
            return false;
        }
        if (index < 0 || index >= _sources.Length || _sources[index] == null)
        {
            Debug.LogError($"[주민] {index + 1}번 생산처가 없습니다.");
            return false;
        }
        return true;
    }

    private void LogAssignment(int index, string action)
    {
        Debug.Log($"[주민] {_resourceAssets[index].Data.DisplayName} 라인 {action} → 이 라인 {_villagerCounts[index]}명 " +
                  $"(예상 생산량 {_baseAmountPerVillager * _villagerCounts[index]}) | 총 배치 {AssignedTotal()}/{maxVillagers}");
    }

    private void TogglePhase()
    {
        if (_phase == Phase.Day)
        {
            int surplus = maxVillagers - AssignedTotal();
            if (surplus > 0)
            {
                Debug.Log($"[페이즈] 잉여 주민이 있어 밤으로 전환할 수 없습니다. " +
                          $"(배치 {AssignedTotal()}/{maxVillagers}, 잉여 {surplus}명 — 모두 배치하세요)");
                return;
            }

            EnterNight();
        }
        else
        {
            EnterDay();
        }
    }

    // 낮 → 밤: 주민 배치 기반 자원 정산 (팀 계약 #5). 정산 트리거 심이 여기서 Produce를 호출한다.
    private void EnterNight()
    {
        _phase = Phase.Night;
        Debug.Log("========== 낮 → 밤: 자원 정산 ==========");

        for (int i = 0; i < _sources.Length; i++)
        {
            if (_sources[i] == null)
            {
                continue;
            }

            int produced = _sources[i].Produce(_villagerCounts[i]);
            Debug.Log($"[정산] {_resourceAssets[i].Data.DisplayName}: 주민 {_villagerCounts[i]}명 → +{produced}");
        }

        Debug.Log("---------- 정산 완료 ----------");
        LogWalletTotal();
        LogPhase();
    }

    // 정산 후 지갑에 쌓인 자원 총량 스냅샷.
    private void LogWalletTotal()
    {
        Debug.Log($"[지갑 총액] 나무 {_wallet.Get(ResourceKind.Wood)} / 철 {_wallet.Get(ResourceKind.Iron)} / " +
                    $"식량 {_wallet.Get(ResourceKind.Food)} / 마나석 {_wallet.Get(ResourceKind.Mana)}");
    }

    // 밤 → 낮: 주민 배치 초기화 + 날짜(웨이브) 증가 (팀 계약 #5).
    private void EnterDay()
    {
        _phase = Phase.Day;
        _day++;

        for (int i = 0; i < _villagerCounts.Length; i++)
        {
            _villagerCounts[i] = 0;
        }

        Debug.Log($"========== 밤 → 낮 (Day {_day}): 주민 배치 초기화 ==========");
        LogPhase();
    }

    private void LogPhase()
    {
        string phaseText = _phase == Phase.Day ? "낮 (주민 배치 가능)" : "밤 (정산 완료, 배치 불가)";
        Debug.Log($"[페이즈] Day {_day} — {phaseText}");
    }

    private void LogHelp()
    {
        Debug.Log($"[도움말] 숫자키 1~N: 라인에 주민 +1 | Shift+숫자키: -1 회수 (총 {maxVillagers}명 한도) | " +
                  "Space: 낮/밤 전환(밤 진입 시 정산)");
    }
}
