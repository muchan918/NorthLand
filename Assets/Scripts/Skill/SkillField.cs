using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;
using NorthLand.Core;

// 전기장(#316)의 장판체. FieldEffect가 감전 착탄 지점에 프리팹으로 생성하고 Init으로 수치를 주입한다.
// SkillBomb과 달리 1회 폭발이 아니라 duration 동안 tickInterval마다 반경 판정을 반복한다.
//
// 핵심: 대상에 상태(StatusEffectHandler DoT)를 붙이지 않는다 — 매 틱 그 순간의 범위 판정 결과에만
// 데미지를 준다. 그래서 적이 장판을 벗어나면 즉시 딜이 끊기고, 재진입하면 다음 틱부터 다시 들어간다.
public class SkillField : MonoBehaviour
{
    [Header("비주얼")]
    [SerializeField] Transform visual;              // 원반 메시. 반경이 바뀌면 코드가 스케일을 맞춘다
    [SerializeField] float visualThickness = 0.02f;

    float damagePerTick;
    float radius;
    float tickInterval;
    LayerMask enemyLayerMask;
    bool debugLog;

    float lifeTimer;
    float tickTimer;
    bool initialized;
    // 반경이 커질수록 한 틱에 걸리는 적이 는다(씬 authoring 18 — 감전 6·폭탄 10보다 넓다).
    readonly List<IDamageable> hits = new List<IDamageable>(64);

    public void Init(float damagePerTick, float radius, float duration, float tickInterval,
                     LayerMask enemyLayerMask, bool debugLog)
    {
        this.damagePerTick = damagePerTick;
        this.radius = radius;

        // 시각 반경과 판정 반경이 어긋나면 "맞을 것 같은데 안 맞는다"가 된다. 코드가 한쪽에서 맞춘다
        // (SkillManager.cs:268이 감전 이펙트를 effectiveRadius에 맞추는 것과 같은 맥락).
        // Cylinder 기본 지름 1 → XZ 스케일 = 반경 x 2. 루트 스케일이 1이어야 성립한다.
        if (visual != null)
            visual.localScale = new Vector3(radius * 2f, visualThickness, radius * 2f);

        this.enemyLayerMask = enemyLayerMask;
        this.debugLog = debugLog;

        // 0 이하 간격이면 아래 while이 무한 루프가 된다. 최소값으로 막는다.
        this.tickInterval = Mathf.Max(0.05f, tickInterval);

        lifeTimer = duration;
        tickTimer = 0f;   // 착탄 즉시 1틱 — 첫 틱을 interval 뒤로 두면 "안 맞았다"로 읽힌다
        initialized = true;

        // 소멸 전 웨이브 종료(밤→낮) 또는 승리/게임오버(런 종료) 시 즉시 정리한다(SkillBomb.cs:27-32과 동일).
        // 매니저가 없으면(예: 테스트 씬) 해당 구독만 스킵하고 장판은 정상 동작한다.
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnNightToDay += HandleWaveEnd;
        if (GameManager.Instance != null)
            GameManager.Instance.OnResultDecided += HandleResultDecided;
    }

    // 웨이브 종료/런 종료 시: 잔류 딜 없이 즉시 소멸(결과 화면 뒤에서 계속 틱이 도는 것 방지).
    void HandleWaveEnd()
    {
        initialized = false;
        Destroy(gameObject);
    }

    void HandleResultDecided(GameResult _) => HandleWaveEnd();

    void OnDestroy()
    {
        if (DayNightManager.Instance != null)
            DayNightManager.Instance.OnNightToDay -= HandleWaveEnd;
        if (GameManager.Instance != null)
            GameManager.Instance.OnResultDecided -= HandleResultDecided;
    }

    void Update()
    {
        if (!initialized) return;   // Init 없이 씬에 놓였거나 이미 무장 해제된 경우

        // 배속(x2/x4)을 따라야 하는 월드 오브젝트라 scaled deltaTime을 쓴다(WL-100의 "월드의 일부인가" 기준).
        float delta = Time.deltaTime;

        tickTimer -= delta;
        // 대입(= tickInterval)이 아니라 누적 차감 — 저프레임/배속에서 틱이 유실되지 않게 한다.
        while (tickTimer <= 0f)
        {
            tickTimer += tickInterval;
            Tick();
        }

        // 소멸 판정은 틱보다 뒤 — 마지막 틱이 잘리지 않는다. (3s / 0.5s → 총 7틱)
        lifeTimer -= delta;
        if (lifeTimer <= 0f)
        {
            initialized = false;
            Destroy(gameObject);
        }
    }

    void Tick()
    {
        SkillHitScan.CollectEnemies(transform.position, radius, enemyLayerMask, hits);

        // Source: 플레이어 스킬 계열은 IAttacker 개체가 아니라 null (SkillManager의 DamageInfo와 동일 규약).
        foreach (var damageable in hits)
            damageable.TakeDamage(new DamageInfo(damagePerTick, null));

        if (debugLog)
            Debug.Log($"[SkillEffect] 전기장 틱: 위치={transform.position}, 적중={hits.Count}마리, 데미지={damagePerTick}, 반경={radius}, 남은 지속={lifeTimer:0.##}s");
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Tick의 캡슐 판정과 같은 모양 — 기즈모만 구체로 두면 수직 범위를 오해한다.
        // 판정이 위아래 양방향이라 캡을 둘 다 그린다(SkillHitScan.VerticalRange 주석 참고).
        Gizmos.color = new Color(0.3f, 0.7f, 1f);
        Vector3 vertical = Vector3.up * SkillHitScan.VerticalRange;
        Gizmos.DrawWireSphere(transform.position - vertical, radius);
        Gizmos.DrawWireSphere(transform.position + vertical, radius);
    }
#endif
}