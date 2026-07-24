namespace NorthLand.Combat
{
    public interface IMovementAgent
    {
        bool IsStopped { get; set; }

        void SetMoveSpeed(float moveSpeed);

        // 슬로우/스턴 인프라(#164): 기준 이동속도에 곱해지는 배율. 1=정상, 0.6=40%감속, 0=완전정지(스턴).
        // StatusEffectHandler가 활성 슬로우/스턴을 합쳐(최댓값 감속) 세팅한다.
        void SetSlowMultiplier(float multiplier);
    }
}
