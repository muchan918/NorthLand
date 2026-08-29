using UnityEngine;

/// 배속(2배)이 켜져 있는 동안 배속 버튼 주위를 도는 별 파티클 연출(#537).
///
/// **상태를 스스로 판단하지 않는다.** 지금 몇 배속인지는 <see cref="GameSpeedController"/>가 알고,
/// 이쪽은 <see cref="Play"/>/<see cref="Stop"/> 두 신호만 받는다 — 배속 상태가 두 곳에 생기면
/// "버튼 표시와 실제 배속이 어긋난다"가 구조적으로 가능해진다(#537 완료 기준).
///
/// ⚠ **시간축은 unscaled다.** 전역 `Time.timeScale`을 타면 일시정지(리워드·설정·튜토리얼) 중
/// 별이 얼어붙는데, 이 연출은 장식이 아니라 "지금 2배속"이라는 **표시**라 멈추는 순간 정보가
/// 사라진다(WL-100의 "플레이어에게 주는 안내·피드백은 unscaled" 기준. `TowerSpawnEffect`와 같은 근거).
/// 프리팹 쪽 설정에 의존하지 않고 <see cref="Awake"/>에서 강제한다 — 파티클이 여러 개라 하나만
/// 빠뜨려도 그 별만 일시정지에서 멎는데, 증상이 프리팹 인스펙터 깊은 곳에 있어 찾기 어렵다.
///
/// ⚠ **끌 때는 남은 별까지 즉시 지운다.** 방출만 멈추고 수명대로 두면(`StopEmitting`) `orbs`의
/// 수명이 5초라 배속을 끈 뒤에도 한참 별이 돌아 **"아직 2배인가?"로 읽힌다.** 표시가 상태보다
/// 오래 남는 것은 이 연출에서 가장 피해야 할 실패다.
public class SpeedBoostEffect : MonoBehaviour
{
    [SerializeField]
    [Tooltip("켜고 끌 파티클 루트. 비우면 이 오브젝트 자신을 쓴다.")]
    private GameObject effectRoot;

    private ParticleSystem[] particles;

    /// 지금 연출이 켜져 있는가. 같은 신호가 두 번 와도 파티클을 다시 뿌리지 않기 위한 상태다.
    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        if (effectRoot == null)
        {
            effectRoot = gameObject;
        }

        // 비활성 자식까지 포함해 찾는다 — 꺼진 채로 시작하는 것이 정상 상태다.
        particles = effectRoot.GetComponentsInChildren<ParticleSystem>(true);

        if (particles.Length == 0)
        {
            Debug.LogError($"[{nameof(SpeedBoostEffect)}] 파티클이 하나도 없습니다.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem.MainModule main = particles[i].main;
            main.useUnscaledTime = true;
        }

        // 초기 상태는 꺼짐. GameSpeedController가 1배로 시작하므로 여기서 맞춰 둔다.
        effectRoot.SetActive(false);
        IsPlaying = false;
    }

    /// 배속이 켜졌다.
    public void Play()
    {
        if (IsPlaying)
        {
            return;
        }

        IsPlaying = true;
        effectRoot.SetActive(true);

        for (int i = 0; i < particles.Length; i++)
        {
            // 이전 재생의 잔여 파티클을 지우고 처음부터 — 껐다 켠 직후 옛 별이 한 프레임 스치는 것을 막는다.
            particles[i].Clear(true);
            particles[i].Play(true);
        }
    }

    /// 배속이 꺼졌다. 남은 별까지 즉시 지운다(위 클래스 주석 참고).
    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;

        for (int i = 0; i < particles.Length; i++)
        {
            particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles[i].Clear(true);
        }

        effectRoot.SetActive(false);
    }
}
