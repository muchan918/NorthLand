// 버프 스킬 시전 1회의 정보(#169). BuffSkillManager가 채워서 BuffResolved 이벤트로 전달하고,
// 구독한 버프 계열 특수효과(BurnBuff 등)가 읽는다.
public class BuffCastContext
{
    public float Duration;   // 버프 지속시간(초)
}
