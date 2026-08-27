using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 지정한 시간이 지나면 충족된다. 아무것도 가르치지 않는 '간격' 전용 조건이다.
//
// 행동을 가르치는 단계에는 쓰지 말 것 — 플레이어가 아무것도 하지 않아도 통과되므로
// 그 단계의 학습이 사라진다. 행동을 요구하는 말풍선에는 이 조건을 쓰지 않는다.
// 예외는 튜토리얼 완료처럼 아무 행동도 요구하지 않고 다음 화면으로 넘기는 전환 통지다.
//
// ⚠ 기본은 게임 속도 배율을 따른다(DelayType.DeltaTime). 그래서 몬스터 스폰 간격과 같은 시계로
//    흐른다. 다만 이 단계에 PauseGameDuringStep을 켜면 시간이 멈춰 영원히 완료되지 않는다 —
//    그 조합이 필요하면 ignoreGameSpeed를 켤 것.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class DelayCondition : TutorialCondition
{
    [Tooltip("이 단계에 들어온 뒤 기다리는 시간(초).")]
    [Min(0f)]
    [SerializeField]
    private float seconds = 2f;

    [Tooltip("켜면 게임 속도·일시정지와 무관하게 실제 시간으로 센다.")]
    [SerializeField]
    private bool ignoreGameSpeed;

    // 조건 객체는 에셋에 저장되므로 Begin에서 새로 만든다.
    private CancellationTokenSource _cts;

    public override void Begin(TutorialContext context)
    {
        // 단계를 벗어나면 대기를 끊어야 한다. 조건은 MonoBehaviour가 아니라 GameObject 파괴에
        // 묶이지 않으므로, 취소 토큰을 직접 들고 있다가 End에서 끊는다.
        // 안 끊으면 이미 지나간 단계의 Fire가 뒤늦게 날아온다.
        _cts = new CancellationTokenSource();

        WaitAsync(_cts.Token).Forget();
    }

    public override void End()
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
    }

    private async UniTaskVoid WaitAsync(CancellationToken token)
    {
        bool canceled = await UniTask
            .Delay(
                TimeSpan.FromSeconds(seconds),
                ignoreGameSpeed ? DelayType.UnscaledDeltaTime : DelayType.DeltaTime,
                cancellationToken: token)
            .SuppressCancellationThrow();

        if (canceled)
        {
            return;
        }

        Fire();
    }
}
