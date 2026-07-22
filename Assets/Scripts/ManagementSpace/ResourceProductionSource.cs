using UnityEngine;

/// <summary>
/// 주민을 배치해 자원을 생산하는 건물의 런타임 생산 단위 (GDD §6.1).<br/>
/// 산출 자원(<see cref="ResourceAsset"/>)만 설정으로 들고, 정산 시 호출부가 넘긴 주민당 생산량·주민 수·
/// 배율을 곱해 생산량을 산출한 뒤 그 결과를 <see cref="ResourceWallet"/>에 넣는다.
/// 자원 흐름(팀 계약 #3)의 "위(생산)" 입구다.<br/>
/// <br/>
/// 경계 심(seam): 주민 수·주민당 생산량은 소유하지 않고 <see cref="Produce"/> 인자로 받으며(주민 시스템 부재,
/// 주민당량은 건물 업그레이드로 가변), 정산 시점도 스스로 정하지 않고 외부의 <see cref="Produce"/> 호출로
/// 트리거된다(낮/밤 시스템 부재).<br/>
/// <br/>
/// (Docs/ManagementArea/Resources.md §1-B·§3.4·§3.5 — 이슈 #42)
/// </summary>
public class ResourceProductionSource
{
    // 통합 시 muchan 파이프라인의 ResourceAsset을 그대로 연결한다. 산출 자원의 ResourceKind는
    // 정산 시점에 OutputResource.Data.Kind에서 해석한다(Data는 호출부가 Start()에서 채우는 규약).
    private readonly ResourceAsset _outputResource;
    private readonly ResourceWallet _wallet;

    public ResourceProductionSource(ResourceAsset outputResource, ResourceWallet wallet)
    {
        if (outputResource == null)
        {
            Debug.LogError("생산처 생성 실패: OutputResource가 null입니다.");
        }
        if (wallet == null)
        {
            Debug.LogError("생산처 생성 실패: ResourceWallet이 null입니다.");
        }

        _outputResource = outputResource;
        _wallet = wallet;
    }

    /// <summary>
    /// 생산량을 계산한다. 부작용 없음. (주민 0명 or 주민당량 0 → 0)<br/>
    /// <paramref name="amountPerVillager"/>는 (업그레이드가 반영된) 주민당 생산량, <paramref name="multiplier"/>는
    /// 패시브 생산 배율(영토 효과 등, 기본 1.0 = 무보정)이다. 곱한 뒤 반올림해 정수로 만든다.
    /// </summary>
    public int CalculateAmount(int villagerCount, int amountPerVillager, float multiplier = 1f)
    {
        if (villagerCount < 0)
        {
            Debug.LogError($"생산량 계산 실패: 주민 수가 음수입니다: {villagerCount}");
            return 0;
        }
        if (amountPerVillager < 0)
        {
            Debug.LogError($"생산량 계산 실패: 주민당 생산량이 음수입니다: {amountPerVillager}");
            return 0;
        }

        return Mathf.RoundToInt(amountPerVillager * villagerCount * multiplier);
    }

    /// <summary>
    /// 배치된 주민 수 × 주민당 생산량만큼 자원을 생산해 지갑에 넣는다(정산). 외부 트리거 전용.<br/>
    /// <paramref name="amountPerVillager"/>는 (업그레이드 반영된) 주민당 생산량, <paramref name="multiplier"/>는
    /// 패시브 생산 배율(기본 1.0). 실제로 지갑에 더한 양을 반환한다. 생산할 수 없으면 0.
    /// </summary>
    public int Produce(int villagerCount, int amountPerVillager, float multiplier = 1f)
    {
        if (_wallet == null)
        {
            Debug.LogError("생산 실패: ResourceWallet이 null입니다.");
            return 0;
        }
        if (_outputResource == null)
        {
            Debug.LogError("생산 실패: OutputResource가 null입니다.");
            return 0;
        }
        if (_outputResource.Data == null)
        {
            Debug.LogError($"생산 실패: OutputResource.Data가 채워지지 않았습니다: {_outputResource.ResourceID} " +
                            "(호출부가 Start()에서 Data를 채우는 규약 — SystemMap §2)");
            return 0;
        }

        int amount = CalculateAmount(villagerCount, amountPerVillager, multiplier);
        if (amount <= 0)
        {
            return 0;
        }

        _wallet.Add(_outputResource.Data.Kind, amount);
        return amount;
    }

    /// <summary>
    /// 건물 에셋에서 자원 생산처를 만든다. 자원을 생산하지 않는 건물이면 false.<br/>
    /// (자원 생산 타입이 아님 / Production 필드 없음 / OutputResource 미지정 → false)<br/>
    /// 주민당 생산량은 여기서 캡처하지 않는다 — 호출부가 <see cref="Produce"/> 시 (업그레이드 반영된) 값을 넘긴다.
    /// </summary>
    public static bool TryCreate(BuildingAsset building, ResourceWallet wallet, out ResourceProductionSource source)
    {
        source = null;

        if (building == null)
        {
            Debug.LogError("생산처 생성 실패: BuildingAsset이 null입니다.");
            return false;
        }
        if (wallet == null)
        {
            Debug.LogError("생산처 생성 실패: ResourceWallet이 null입니다.");
            return false;
        }
        if (building.BuildingType != BuildingType.Production)
        {
            Debug.LogError($"생산처 생성 실패: 자원 생산 건물이 아닙니다: {building.BuildingID} ({building.BuildingType})");
            return false;
        }

        BuildingAsset.ProductionFields production = building.Production;
        if (production == null)
        {
            Debug.LogError($"생산처 생성 실패: Production 필드가 없습니다: {building.BuildingID}");
            return false;
        }
        if (production.OutputResource == null)
        {
            Debug.LogError($"생산처 생성 실패: OutputResource가 지정되지 않았습니다: {building.BuildingID}");
            return false;
        }

        source = new ResourceProductionSource(production.OutputResource, wallet);
        return true;
    }
}
