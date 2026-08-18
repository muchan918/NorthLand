using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

// 스킬 범위 판정의 단일 출처(#398). 감전·폭탄·전기장이 같은 규칙으로 적을 모은다 —
// SkillStatsFormatter가 표기를 한 곳에 모으는 것과 같은 계보.
public static class SkillHitScan
{
    // 시전면과 몬스터 부양 높이가 다르다: 시전은 y=2 평면(SkillButtonView._castHeight)인데
    // 지상 몬스터는 타일 표면 + monsterWaypointYOffset(CombatMapTileSpawner, 코드 기본값 6f ·
    // 씬 authoring 3.2 — 값이 씬에 있어 여기서 파생할 수 없다, WL-063/WL-149),
    // 공중은 거기서 +4(FlyingMonsterMove.altitude)를 더 뜬다. 수평 반경으로 이 차이를 덮으면
    // 원반 인디케이터보다 넓게 맞으므로, 축을 나눠 수직만 연다.
    //
    // 위아래 양쪽으로 여는 이유: _castHeight는 씬에서만 2고 스크립트 기본값은 20이라
    // 프리팹 리셋·신규 씬에서 시전면이 몬스터보다 위로 갈 수 있다. 위쪽만 열면 그때 전 스킬이
    // 조용히 빗나간다. 시전면 아래엔 적이 없으므로 아래로 여는 대가는 없다.
    //
    // public인 이유: 전기장 기즈모(SkillField.OnDrawGizmosSelected)가 실제 판정과 같은 모양을
    // 그려야 한다. 기즈모가 자기 값을 따로 들면 판정과 갈려서, 눈으로 보는 범위가 거짓이 된다.
    public const float VerticalRange = 12f;

    // NonAlloc API는 배열만 받는다(호출마다 새 배열을 만들지 않는 것이 존재 이유). 대신 버퍼가
    // 차면 초과분을 조용히 버리므로, 아래 CollectEnemies가 포화 시 2배로 키워 다시 친다 —
    // readonly가 아닌 이유. 한 번 커진 버퍼는 유지되므로 평상시 할당은 0이다.
    static Collider[] s_Buffer = new Collider[64];

    // 한 몬스터가 콜라이더를 둘 이상 가지면 같은 대상이 여러 번 잡힌다. 그대로 두면 데미지가
    // 중복 적용되고, 감전은 이 목록을 보상 특수효과(화상·처형)에 넘기므로 거기까지 번진다.
    static readonly HashSet<IDamageable> s_Seen = new HashSet<IDamageable>();

    /// 시전 지점 주변의 살아있는 적을 <paramref name="results"/>에 모은다(호출 시 비운다).
    public static void CollectEnemies(Vector3 center, float radius, LayerMask enemyLayerMask,
                                      List<IDamageable> results)
    {
        results.Clear();
        s_Seen.Clear();

        Vector3 vertical = Vector3.up * VerticalRange;

        // 버퍼가 꽉 차면 초과분이 잘렸을 수 있다. NonAlloc은 그걸 알려주지 않으므로 키워서 다시 친다.
        // "정확히 꽉 참"일 때도 한 번 헛돌지만 결과는 같고, 감전은 연구소 Lv3+에서 반경이 27까지
        // 커져 64가 생각보다 빨리 찬다 — 밀집 웨이브에서 몇 마리가 빠지는 건 재현이 안 되는 버그다.
        int count;
        while (true)
        {
            count = Physics.OverlapCapsuleNonAlloc(center - vertical, center + vertical, radius,
                                                   s_Buffer, enemyLayerMask);
            if (count < s_Buffer.Length) break;
            s_Buffer = new Collider[s_Buffer.Length * 2];
        }

        for (int i = 0; i < count; i++)
        {
            var damageable = s_Buffer[i].GetComponentInParent<IDamageable>();
            if (damageable == null || damageable.Faction != Faction.Enemy || damageable.IsDead) continue;
            if (!s_Seen.Add(damageable)) continue;   // 같은 몬스터의 두 번째 콜라이더
            results.Add(damageable);
        }
    }
}