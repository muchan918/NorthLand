using System;

// 튜토리얼 한 단계의 '완료 조건'.
// 진행부(TutorialController)는 이 조건이 무엇을 감시하는지 모른다 — Satisfied만 받는다.
// 새 시스템의 단계를 붙일 때는 이 클래스를 상속한 조건 하나만 추가하면 되고,
// 진행부·오버레이는 건드리지 않는다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 데이터가 깨진다.
[Serializable]
public abstract class TutorialCondition
{
    // 조건이 충족됐다. 구독자는 진행부 하나뿐이다.
    public event Action Satisfied;

    // 감시를 시작한다. 상태를 가진 조건은 여기서 반드시 초기화한다 —
    // 조건 객체는 에셋에 저장되므로 이전 플레이의 값이 남아 있을 수 있다.
    public abstract void Begin(TutorialContext context);

    // 감시를 끝낸다. Begin에서 건 구독은 여기서 전부 푼다.
    public abstract void End();

    protected void Fire()
    {
        Satisfied?.Invoke();
    }
}