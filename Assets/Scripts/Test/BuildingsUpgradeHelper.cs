using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// [테스트 전용] 건물 업그레이드(#139) 수동 검증 헬퍼. 씬의 아무 GameObject에 붙여 PlayMode에서 쓴다.
/// - <b>Space</b>: 나무·철을 목표량(<see cref="_grantAmount"/>, 기본 999)으로 채운다(업그레이드 비용 시드).
/// - <b>Alpha1/2/3</b>: 나무꾼의 집(나무)·광산(철)·농장(식량) 라인 업그레이드를 시도한다.
/// - 모든 동작을 <see cref="Debug.Log"/>로 남긴다.
///
/// 지갑에 자원을 넣는 public API가 없어(<see cref="ManagementController"/>는 소비 게이트웨이만 노출) 테스트 한정으로
/// 리플렉션으로 <see cref="ResourceWallet"/>에 접근한다. 입력은 신형 Input System 사용(레거시 Input 금지 — 프로젝트 규약).
/// 프로덕션 코드가 아니라 Assets/Scripts/Test 소속.
/// </summary>
public class BuildingsUpgradeHelper : MonoBehaviour
{
    [Tooltip("대상 ManagementController. 비워두면 씬에서 자동 탐색한다.")]
    [SerializeField] ManagementController _controller;

    [Tooltip("Space로 채워줄 자원 목표량.")]
    [SerializeField] int _grantAmount = 999;

    ResourceWallet _wallet;

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || !EnsureController())
        {
            return;
        }

        if (kb.spaceKey.wasPressedThisFrame)
        {
            GrantResources();
        }
        if (kb.digit1Key.wasPressedThisFrame)
        {
            TryUpgrade("나무꾼의 집", ResourceKind.Wood);
        }
        if (kb.digit2Key.wasPressedThisFrame)
        {
            TryUpgrade("광산", ResourceKind.Iron);
        }
        if (kb.digit3Key.wasPressedThisFrame)
        {
            TryUpgrade("농장", ResourceKind.Food);
        }
    }

    // 나무·철을 목표량까지 채운다(부족분만 Add → 지갑 OnChanged가 발화돼 UI도 함께 갱신).
    void GrantResources()
    {
        ResourceWallet wallet = GetWallet();
        if (wallet == null)
        {
            Debug.LogWarning("[업그레이드테스트] ResourceWallet 접근 실패 — Space 무시.");
            return;
        }

        TopUp(wallet, ResourceKind.Wood, _grantAmount);
        TopUp(wallet, ResourceKind.Iron, _grantAmount);
        Debug.Log($"[업그레이드테스트] 자원 지급 → 나무 {_controller.ResourceCount(ResourceKind.Wood)} / 철 {_controller.ResourceCount(ResourceKind.Iron)}");
    }

    static void TopUp(ResourceWallet wallet, ResourceKind kind, int target)
    {
        int deficit = target - wallet.Get(kind);
        if (deficit > 0)
        {
            wallet.Add(kind, deficit);
        }
    }

    // 지정 자원을 생산하는 라인을 찾아 업그레이드를 시도하고 결과를 로그로 남긴다.
    void TryUpgrade(string label, ResourceKind kind)
    {
        int index = FindLineByKind(kind);
        if (index < 0)
        {
            Debug.LogWarning($"[업그레이드테스트] {label}({kind}) 라인을 찾지 못했습니다.");
            return;
        }

        int beforeLevel = _controller.LineLevel(index);
        if (_controller.TryUpgrade(index))
        {
            Debug.Log($"[업그레이드테스트] {label} 업그레이드 성공: Lv{beforeLevel} → Lv{_controller.LineLevel(index)} " +
                                $"(주민당량 {_controller.LineAmountPerVillager(index)}, 최대 Lv{_controller.LineMaxLevel(index)})");
        }
        else
        {
            Debug.LogWarning($"[업그레이드테스트] {label} 업그레이드 실패 — CanUpgrade={_controller.CanUpgrade(index)}, " +
                                        $"Lv{_controller.LineLevel(index)}/{_controller.LineMaxLevel(index)}, IsDay={_controller.IsDay} " +
                                        $"(보유: 나무 {_controller.ResourceCount(ResourceKind.Wood)} / 철 {_controller.ResourceCount(ResourceKind.Iron)})");
        }
    }

    int FindLineByKind(ResourceKind kind)
    {
        for (int i = 0; i < _controller.LineCount; i++)
        {
            if (_controller.LineKind(i) == kind)
            {
                return i;
            }
        }
        return -1;
    }

    bool EnsureController()
    {
        if (_controller != null)
        {
            return true;
        }
        _controller = FindFirstObjectByType<ManagementController>();
        if (_controller == null)
        {
            Debug.LogWarning("[업그레이드테스트] ManagementController를 씬에서 찾지 못했습니다.");
            return false;
        }
        return true;
    }

    // 지갑은 public 획득 API가 없어 테스트 한정 리플렉션으로 가져온다(세션 내 참조가 안정적이라 캐시).
    ResourceWallet GetWallet()
    {
        if (_wallet != null)
        {
            return _wallet;
        }
        FieldInfo field = typeof(ManagementController).GetField("_wallet", BindingFlags.NonPublic | BindingFlags.Instance);
        _wallet = field?.GetValue(_controller) as ResourceWallet;
        return _wallet;
    }
}
