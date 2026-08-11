using System.Reflection;
using UnityEngine;

/// <summary>
/// [테스트 전용] 밸런스 반복 검증용 헬퍼 패널. 씬의 빈 GameObject에 붙이고, uGUI Button의 onClick에
/// 아래 public 메서드를 연결해서 PlayMode에서 클릭으로 쓴다(키보드 단축키 불필요).
/// - <see cref="ForceWaveClear"/>: 남은 웨이브를 즉시 클리어(대기 스폰 중단 + 현재 몬스터 제거 + 정상 완료 경로).
/// - <see cref="GrantWood"/>/<see cref="GrantIron"/>/<see cref="GrantFood"/>/<see cref="GrantMana"/>:
///   해당 자원을 <see cref="_grantAmount"/>(기본 20)만큼 지급. 자원은 이 4종뿐이다(#337).
/// - <see cref="SkipDay"/>: 주민 배치·몬스터 스폰 없이 웨이브 수만 올리고 다음 날로 넘어간다(<see cref="DayNightManager.SkipDay"/>).
///
/// 지갑에 자원을 넣는 public API가 없어(<see cref="ManagementController"/>는 소비 게이트웨이만 노출)
/// <see cref="BuildingsUpgradeHelper"/>와 동일하게 테스트 한정 리플렉션으로 <see cref="ResourceWallet"/>에 접근한다.
/// 프로덕션 코드가 아니라 Assets/Scripts/Test 소속.
/// </summary>
public class BalanceTestPanel : MonoBehaviour
{
    [Tooltip("필수: 인스펙터에서 드래그해 할당한다.")]
    [SerializeField] MonsterSpawn _spawn;

    [Tooltip("필수: 인스펙터에서 드래그해 할당한다.")]
    [SerializeField] ManagementController _controller;

    [Tooltip("자원 버튼 1회 클릭당 지급량.")]
    [SerializeField] int _grantAmount = 20;

    ResourceWallet _wallet;

    [SerializeField]
    private GameObject Panel;


    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F4))
        {
            Panel.SetActive(!Panel.activeSelf);
        }
    }














    // ── 웨이브 ──────────────────────────────────────────────
    public void ForceWaveClear()
    {
        if (_spawn == null)
        {
            Debug.LogWarning("[밸런스테스트] MonsterSpawn이 연결되지 않았습니다. 인스펙터에서 드래그해 할당하세요.");
            return;
        }

        _spawn.ForceClearWave();
        Debug.Log("[밸런스테스트] 웨이브 클리어 강제 실행");
    }

    // ── 자원 지급 ───────────────────────────────────────────
    public void GrantWood() => Grant(ResourceKind.Wood, "나무");
    public void GrantIron() => Grant(ResourceKind.Iron, "철");
    public void GrantFood() => Grant(ResourceKind.Food, "식량");
    public void GrantMana() => Grant(ResourceKind.Mana, "마나");

    void Grant(ResourceKind kind, string label)
    {
        ResourceWallet wallet = GetWallet();
        if (wallet == null)
        {
            Debug.LogWarning($"[밸런스테스트] ResourceWallet 접근 실패 — {label} 지급 무시.");
            return;
        }

        wallet.Add(kind, _grantAmount);
        Debug.Log($"[밸런스테스트] {label} +{_grantAmount} → 보유 {wallet.Get(kind)}");
    }

    // ── 날짜 스킵 ───────────────────────────────────────────
    public void SkipDay()
    {
        if (DayNightManager.Instance == null)
        {
            Debug.LogWarning("[밸런스테스트] DayNightManager가 없습니다.");
            return;
        }

        DayNightManager.Instance.SkipDay();
        Debug.Log($"[밸런스테스트] Skip Day 실행 → Wave {DayNightManager.Instance.WaveCount}");
    }

    // 지갑은 public 획득 API가 없어 테스트 한정 리플렉션으로 가져온다(세션 내 참조가 안정적이라 캐시).
    ResourceWallet GetWallet()
    {
        if (_wallet != null)
        {
            return _wallet;
        }
        if (_controller == null)
        {
            Debug.LogWarning("[밸런스테스트] ManagementController가 연결되지 않았습니다. 인스펙터에서 드래그해 할당하세요.");
            return null;
        }
        FieldInfo field = typeof(ManagementController).GetField("_wallet", BindingFlags.NonPublic | BindingFlags.Instance);
        _wallet = field?.GetValue(_controller) as ResourceWallet;
        return _wallet;
    }



}
