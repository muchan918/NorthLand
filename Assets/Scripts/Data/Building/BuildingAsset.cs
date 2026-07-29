using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BuildingAsset", menuName = "Scriptable Objects/BuildingAsset")]
public class BuildingAsset : ScriptableObject
{
    public string BuildingID;

    // BuildingAssetEditor가 Play 이전 편집 모드에서 필드 그룹을 골라 보여줘야 하므로
    // ResourceAsset과 달리 Data 캐시가 아닌 일반 필드로 노출한다.
    public BuildingType BuildingType;

    [HideInInspector]
    public BuildingData Data;

    public List<ResourceCost> Cost;

    public ProductionFields Production;
    public SkillFields Skill;
    public ExchangeFields Exchange;
    public VillagerFields Villager;

#if UNITY_EDITOR
    // 에디터 authoring 가드: 업그레이드 수치를 잘못 적으면 조용히 생산 하락/무료 업그레이드가 되므로 경고한다(수치=SO, WL-015 축).
    private void OnValidate()
    {
        ValidateProductionUpgrades();
        ValidateUpgradeCosts();
        ValidateExchangeOffers();
        ValidateVillagerCosts();
    }

    // 생산 건물: 주민당량이 base(레벨0)부터 단조 증가하지 않으면 경고한다.
    // AmountPerVillager가 절대값이라 실수로 낮게/비단조로 적으면 업그레이드가 조용히 생산을 떨어뜨릴 수 있다.
    private void ValidateProductionUpgrades()
    {
        if (BuildingType != BuildingType.Production || Production == null || Production.UpgradeLevels == null)
        {
            return;
        }

        int prev = Production.BaseAmountPerVillager;
        for (int i = 0; i < Production.UpgradeLevels.Count; i++)
        {
            int cur = Production.UpgradeLevels[i].AmountPerVillager;
            if (cur <= prev)
            {
                Debug.LogWarning($"[BuildingAsset] {BuildingID}: 업그레이드 Lv{i + 1} 주민당량({cur})이 이전 단계({prev}) 이하 — 업그레이드가 생산을 늘리지 않습니다.", this);
            }
            prev = cur;
        }
    }

    // 업그레이드 전용 건물(마법 연구소 등): 레벨 비용이 비었거나 총액 0이면 경고한다.
    // 빈 비용은 ManagementController.TrySpend가 무료 성공을 반환해 조용히 '무료 업그레이드'가 된다
    // (WL-057 poison_tower 무료 배치와 동일 실패 모드 — 비용 authoring 누락을 조기에 드러낸다).
    // ⚠ 게이트는 BuildingType이 아니라 '업그레이드 데이터 존재'로 건다 — 등록 경로
    // (ManagementController.BuildUpgradeBuildings)가 BuildingType과 무관하게 Skill.UpgradeLevels를
    // 읽으므로 검증도 같은 키여야 한다(필드가 타입 중립 그룹으로 승격돼도 가드가 자동으로 따라온다).
    // Skill 타입이 아닌 건물은 UpgradeLevels가 비어 자연히 스킵된다.
    private void ValidateUpgradeCosts()
    {
        if (Skill == null || Skill.UpgradeLevels == null || Skill.UpgradeLevels.Count == 0)
        {
            return;
        }

        for (int i = 0; i < Skill.UpgradeLevels.Count; i++)
        {
            SkillUpgradeLevel level = Skill.UpgradeLevels[i];
            int total = 0;
            if (level != null && level.Cost != null)
            {
                for (int c = 0; c < level.Cost.Count; c++)
                {
                    ResourceCost cost = level.Cost[c];
                    if (cost != null && cost.Resource != null && cost.Amount > 0)
                    {
                        total += cost.Amount;
                    }
                }
            }
            if (total <= 0)
            {
                Debug.LogWarning($"[BuildingAsset] {BuildingID}: 업그레이드 Lv{i + 1} 비용이 비어 있습니다(총액 0) — 무료로 업그레이드됩니다. 마나석 등 비용을 설정하세요.", this);
            }
        }
    }

    // 교환 건물(연금술사의 집 등): 교환 행이 무료(PayAmount 0)이거나 아무것도 주지 않으면(GainAmount 0) 경고한다.
    // 무료 교환은 무한 자원 획득 경로라 위 무료 업그레이드보다 파급이 크다(#211).
    // ⚠ 게이트는 BuildingType이 아니라 '교환 데이터 존재'로 건다 — 진입 경로(BuildingInfo.HasExchange)와 같은 키여야 한다.
    private void ValidateExchangeOffers()
    {
        if (Exchange == null || Exchange.Offers == null || Exchange.Offers.Count == 0)
        {
            return;
        }

        if (Exchange.PayResource == null)
        {
            Debug.LogWarning($"[BuildingAsset] {BuildingID}: 교환 지불 자원(PayResource)이 비어 있습니다 — 교환이 동작하지 않습니다.", this);
        }

        for (int i = 0; i < Exchange.Offers.Count; i++)
        {
            ExchangeOffer offer = Exchange.Offers[i];
            if (offer == null)
            {
                continue;
            }
            if (offer.GainResource == null)
            {
                Debug.LogWarning($"[BuildingAsset] {BuildingID}: 교환 {i + 1}번 행의 획득 자원이 비어 있습니다.", this);
                continue;
            }
            if (offer.PayAmount <= 0)
            {
                Debug.LogWarning($"[BuildingAsset] {BuildingID}: 교환 {i + 1}번 행({offer.GainResource.ResourceID})의 지불량이 0 이하 — 무료로 자원을 얻게 됩니다.", this);
            }
            if (offer.GainAmount <= 0)
            {
                Debug.LogWarning($"[BuildingAsset] {BuildingID}: 교환 {i + 1}번 행({offer.GainResource.ResourceID})의 획득량이 0 이하 — 지불만 하고 아무것도 얻지 못합니다.", this);
            }
            if (Exchange.PayResource != null && offer.GainResource == Exchange.PayResource)
            {
                Debug.LogWarning($"[BuildingAsset] {BuildingID}: 교환 {i + 1}번 행의 획득 자원이 지불 자원과 같습니다 — 자기 자신 교환은 의미가 없습니다.", this);
            }
        }
    }

    // 주민 증가 건물(본진, #227): 회차 비용이 비었거나 총액 0이면 경고한다.
    // 빈 비용은 ManagementController.TrySpend가 무료 성공을 반환해 조용히 '공짜 주민'이 된다
    // (ValidateUpgradeCosts와 같은 실패 모드 — 비용 authoring 누락을 조기에 드러낸다).
    // ⚠ 게이트는 BuildingType이 아니라 '주민 증가 데이터 존재'로 건다 — 진입 경로(BuildingInfo)와 같은 키여야 한다.
    private void ValidateVillagerCosts()
    {
        if (Villager == null || Villager.Levels == null || Villager.Levels.Count == 0)
        {
            return;
        }

        for (int i = 0; i < Villager.Levels.Count; i++)
        {
            VillagerGrowthLevel level = Villager.Levels[i];
            int total = 0;
            if (level != null && level.Cost != null)
            {
                for (int c = 0; c < level.Cost.Count; c++)
                {
                    ResourceCost cost = level.Cost[c];
                    if (cost != null && cost.Resource != null && cost.Amount > 0)
                    {
                        total += cost.Amount;
                    }
                }
            }
            if (total <= 0)
            {
                Debug.LogWarning($"[BuildingAsset] {BuildingID}: 주민 증가 {i + 1}회차 비용이 비어 있습니다(총액 0) — 주민이 무료로 늘어납니다. 비용을 설정하세요.", this);
            }
        }
    }
#endif

    [System.Serializable]
    public class ProductionFields
    {
        // 레벨 0(미업그레이드) 주민당 생산량. 업그레이드하면 UpgradeLevels의 값으로 올라간다.
        public int BaseAmountPerVillager;

        // 나무꾼의 집/광산/농장처럼 ResourceTable 자원을 생산하는 경우만 채운다.
        public ResourceAsset OutputResource;

        // 건물 업그레이드 레벨 테이블. index i = 레벨 (i+1). 비어 있으면 업그레이드 불가(최대 레벨 = Count).
        // 수치(비용·주민당량)는 이 SO에 직접 authoring한다(밸런싱 TBD — 영토 효과·타워 스탯 선례, WL-015).
        public List<UpgradeLevel> UpgradeLevels = new List<UpgradeLevel>();
    }

    // 생산 건물 업그레이드 한 단계. AmountPerVillager는 누적 델타가 아니라 그 레벨의 절대 주민당량.
    [System.Serializable]
    public class UpgradeLevel
    {
        // 이 레벨에 도달하기 위해 소모하는 비용(ManagementController.TrySpend 게이트웨이 경유).
        public List<ResourceCost> Cost = new List<ResourceCost>();

        // 이 레벨에서의 주민당 생산량(절대값).
        public int AmountPerVillager;
    }

    [System.Serializable]
    public class SkillFields
    {
        public ResourceAsset InputResource;

        // 마법 연구소 등 스킬 건물의 업그레이드 레벨 테이블. index i = 레벨 (i+1). 비어 있으면 업그레이드 불가(최대 레벨 = Count).
        // 도달 비용(마나석)과 강화 배율을 같은 리스트에 authoring한다(#205) — 비용과 배율이 서로 다른 파일에
        // 나뉘어 레벨 개수가 어긋나는 문제(PR#216 리뷰)를 원천 차단. 배율은 소비 시스템(SkillManager/
        // BuffSkillManager)이 자기가 쓸 필드만 읽는다(결합도 최소, BuildingUpgrade.md §1·§8).
        public List<SkillUpgradeLevel> UpgradeLevels = new List<SkillUpgradeLevel>();
    }

    // 스킬/업그레이드 전용 건물(마법 연구소 등)의 업그레이드 한 단계. 생산 건물의 UpgradeLevel과 달리
    // 주민당량 필드가 없다 — 도달 비용 + 소비 시스템이 참조하는 강화 배율을 갖는다.
    [System.Serializable]
    public class SkillUpgradeLevel
    {
        // 이 레벨에 도달하기 위해 소모하는 비용(ManagementController.TrySpend 게이트웨이 경유). 마법 연구소는 마나석.
        public List<ResourceCost> Cost = new List<ResourceCost>();

        // 감전(SkillManager, #205) 기본 스탯 배율. 1 = 강화 없음(효과 없음).
        public float DamageMultiplier = 1f;
        public float RadiusMultiplier = 1f;
        public float CooldownMultiplier = 1f;

        // 버프(BuffSkillManager, #205) 기본 스탯 배율. 1 = 강화 없음(효과 없음).
        public float BuffDamageMultiplierScale = 1f;
        public float BuffAttackSpeedMultiplierScale = 1f;
        public float BuffDurationMultiplier = 1f;
        public float BuffCooldownMultiplier = 1f;
    }

    // 교환 건물(연금술사의 집, #211). 마나석 등 지불 자원 1종을 여러 자원으로 바꿔주는 단방향 상점.
    // 생산 라인·업그레이드 트랙과 별개 축이라 필드 그룹을 따로 둔다(BuildingAssetEditor가 Store 타입에서만 그린다).
    [System.Serializable]
    public class ExchangeFields
    {
        // 모든 교환 행이 공통으로 소모하는 자원(연금술사의 집 = 마나석). 행마다 지불 자원이 다를 일은 없다.
        public ResourceAsset PayResource;

        // 교환 행 목록. 행 추가/삭제는 코드 변경 없이 이 리스트만 편집하면 된다(대상 자원 범위 조정이 인스펙터 작업).
        public List<ExchangeOffer> Offers = new List<ExchangeOffer>();

        // 교환 효율 업그레이드 테이블(GDD: 업그레이드 시 획득량 증가). #211 범위 밖 — 지금은 비워 둔다.
        // ⚠ 이 리스트를 실제로 쓰려면 선행 작업이 필요하다: ManagementController.BuildUpgradeBuildings가
        //   BuildingType과 무관하게 Skill.UpgradeLevels만 하드코딩으로 읽으므로, 레벨 테이블을 타입 중립
        //   소스로 승격해야 한다(BuildingUpgrade.md §8 — 본성 업그레이드와 공유하는 선행 작업).
        public List<ExchangeUpgradeLevel> UpgradeLevels = new List<ExchangeUpgradeLevel>();
    }

    // 교환 한 줄. "PayResource를 PayAmount만큼 내고 GainResource를 GainAmount만큼 받는다".
    [System.Serializable]
    public class ExchangeOffer
    {
        public ResourceAsset GainResource;
        public int PayAmount;
        public int GainAmount;
    }

    // 교환 효율 업그레이드 한 단계(#211 범위 밖, 자리만 확보).
    // 비용과 효과를 같은 리스트에 두는 건 SkillUpgradeLevel과 같은 이유다(레벨 개수 어긋남 원천 차단, PR#216 리뷰).
    [System.Serializable]
    public class ExchangeUpgradeLevel
    {
        public List<ResourceCost> Cost = new List<ResourceCost>();

        // 획득량 배율. 1 = 강화 없음. (예: 1.5면 마나 10 → 나무 10 이 나무 15가 된다)
        public float GainMultiplier = 1f;
    }

    // 주민 수 증가(본진, #227). 자원을 소모해 총 보유 주민 수를 1명씩 늘린다.
    // 생산 라인·업그레이드 트랙과 별개 축이라 필드 그룹을 따로 둔다 — 기존 업그레이드 트랙
    // (ManagementController._upgradeBuildings)에 태우지 않으므로 레벨 테이블 타입 중립 승격이 필요 없다.
    [System.Serializable]
    public class VillagerFields
    {
        // 주민 증가 비용 테이블. index i = i+1회차 증가.
        // ⚠ 상한을 별도 필드로 두지 않는다 — 행 수가 곧 증가 가능 횟수다(시작 2명 + 8행 = 최대 10명).
        //   상한과 비용이 한 곳에 있어 서로 어긋날 수 없다(UpgradeLevels와 같은 방식).
        //   행을 모두 소진하면 비용 조회가 null을 반환하고 표시부가 MAX로 전환된다.
        public List<VillagerGrowthLevel> Levels = new List<VillagerGrowthLevel>();
    }

    // 주민 1명을 늘리는 한 단계. UpgradeLevel/SkillUpgradeLevel과 달리 효과값 필드가 없다
    // — 증가량은 항상 1로 고정이라 authoring할 수치가 비용뿐이다.
    [System.Serializable]
    public class VillagerGrowthLevel
    {
        // 이 회차에 소모하는 비용(ManagementController.TrySpend 게이트웨이 경유, WL-017/WL-048).
        // 리스트라서 회차가 오를수록 요구 자원 종류를 늘릴 수 있다(예: 나무 20 + 철 10).
        public List<ResourceCost> Cost = new List<ResourceCost>();
    }
}
