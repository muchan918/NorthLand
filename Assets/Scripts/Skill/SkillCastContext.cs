using System.Collections.Generic;
using UnityEngine;
using NorthLand.Combat;

// 감전 스킬 임팩트 1회의 정보(#169). SkillManager가 채워서 ImpactResolved 이벤트로 전달하고,
// 구독한 특수효과(SkillEffect)들이 읽는다. HitTargets는 임팩트마다 재사용되는 버퍼라
// 이벤트 처리 중에만 유효하다 — 보관하지 말 것.
public class SkillCastContext
{
    public Vector3 Position;                       // 착탄 지점
    public IReadOnlyList<IDamageable> HitTargets;  // 이번 임팩트에 피해를 준 적들
}