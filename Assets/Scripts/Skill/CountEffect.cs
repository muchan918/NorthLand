using UnityEngine;

// 추가시전(#169 → #319 재설계): 감전의 최대 충전을 레벨만큼 올린다. 충전 보유·소모·재적립은
// SkillManager가 소유하고(시계 하나, 간격당 1발 — 롤 티모 버섯과 같은 규칙), 이 효과는 레벨을
// 밀어넣기만 한다. 예전처럼 같은 자리를 자동으로 또 때리지 않으므로 착탄 지점을 고르는 판단이
// 플레이어에게 남는다.
public class CountEffect : SkillEffect
{
    public override WaveRewardType Type => WaveRewardType.ExtraCast;

    // 보상 패널(#287) 표시용. 기본 1발 + 보너스 Level개 = 총 1 + Level발.
    // 스킬 버튼의 "2/2" 표기와 같은 셈법이라 두 화면이 어긋나지 않는다.
    public int GetCurrentCount() => GetCountAt(Level);
    public int GetCountAt(int level) => 1 + level;

    public override string GetStatSummary()
        => SkillStatsFormatter.BuildChargeCountLine(GetCurrentCount(), GetCountAt(NextLevel));

    // 보상 획득·세이브 복원 양쪽에서 호출된다(SkillEffect가 두 경로를 여기로 모은다).
    protected override void OnLevelChanged()
        => SkillManager.Instance?.SetBonusCharges(Level);
}
