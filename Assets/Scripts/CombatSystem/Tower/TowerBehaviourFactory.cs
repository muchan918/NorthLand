using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Combat
{
    // TowerAsset을 보고 그 타워가 가질 행동을 조립한다.
    //
    // **TowerType/MagicEffectType switch가 사는 유일한 곳**이다. 예전에는 같은 분기가 Tower(공격 스탯 해석,
    // 명중 구성)와 AuraTower(Buff/Debuff 6곳)에 흩어져 있어서, 타워 종류를 하나 늘리면 그 전부를 손봐야 했다.
    // 새 타워 종류 = ITowerBehaviour 구현 1개 + 여기 분기 1줄.
    public static class TowerBehaviourFactory
    {
        // 이 타워의 공격 스탯. **오라 타워는 null**이고, 그 null이 "이 타워는 공격하지 않는다"의 유일한 신호다.
        // 이 해석이 필요한 곳이 여럿이라(행동 조립·툴팁·정보 패널) 단일 출처로 둔다(WL-079).
        //
        // ⚠ `asset.Attack`을 그냥 돌려주면 안 된다. Unity 직렬화는 [Serializable] 클래스 필드에 null을
        // 허용하지 않아 **오라 타워에서도 non-null**이다(lightning_tower가 Chain인데 Single.Attack이 0으로
        // 채워져 있는 것이 그 증거). 그대로 두면 아래 Create의 가드가 항상 참이 되어 오라 타워에도
        // AttackBehaviour가 붙고, `Has<AttackBehaviour>()`가 true가 되면서:
        //   ① 보스 P3 마력 봉인이 오라 타워를 노린다 — "봉인 중에도 감속은 살아남는다"는 P1 파훼 설계가 깨짐
        //   ② 버프 오라끼리 서로를 버프한다 — 반경이 사거리 축을 공유해 순서 의존 피드백 고리가 되살아남
        // 둘 다 컴파일러도 런타임도 잡지 못한다(예외 없이 밸런스만 조용히 뒤집힌다).
        //
        // TODO(#274 Phase 2): 프리팹의 Actions 리스트가 종류의 정본이 되면 이 분기 자체가 사라진다.
        public static TowerAsset.AttackFields ResolveAttackFields(TowerAsset asset)
        {
            if (asset == null) return null;
            return asset.TowerType == TowerType.Magic ? null : asset.Attack;
        }

        // 이 SO가 요구하는 행동들을 host에 부착하고 초기화하지 않은 상태로 돌려준다(초기화는 Tower.Build가 한다).
        // 부착 순서 = Tick 순서다. 지금은 서로 독립이라 의미가 없지만, 순서가 의미를 갖게 되면 여기가 그 정의 지점이다.
        public static void Create(GameObject host, TowerAsset asset, List<ITowerBehaviour> result)
        {
            result.Clear();
            if (host == null || asset == null) return;

            switch (asset.TowerType)
            {
                case TowerType.Single:
                case TowerType.Area:
                case TowerType.Chain:
                    // 공격 스탯이 비어 있으면 행동을 붙이지 않는다 — 무동작 컴포넌트를 남기지 않고
                    // "이 타워는 공격하지 않는다"가 Has<AttackBehaviour>() 한 번으로 드러나게 한다.
                    if (ResolveAttackFields(asset) != null)
                    {
                        result.Add(GetOrAdd<AttackBehaviour>(host));
                    }
                    break;

                case TowerType.Magic:
                    // Buff/Debuff는 서로 다른 행동이다. 예전에는 한 컴포넌트가 MagicEffectType으로
                    // 6곳에서 분기했는데, 그 분기가 전부 이 한 줄로 접힌다.
                    // 예전의 `asset.Magic?.XxxAura != null` 가드는 제거했다 — Unity가 [Serializable] 클래스
                    // 필드를 항상 인스턴스화하므로 그 조건은 처음부터 항상 참이었다(무의미한 가드).
                    switch (asset.MagicEffectType)
                    {
                        case MagicEffectType.Buff:
                            result.Add(GetOrAdd<BuffAuraBehaviour>(host));
                            break;
                        case MagicEffectType.Debuff:
                            result.Add(GetOrAdd<DebuffAuraBehaviour>(host));
                            break;
                        default:
                            // MagicEffectType 미선택은 SO 저작 실수다. 예전에는 TowerAssetEditor의
                            // HelpBox가 먼저 경고했지만 그 커스텀 에디터는 #274 Phase 1에서 삭제됐다
                            // (평탄 스키마를 인스펙터에서 가리고 있었다) — 지금은 이 로그가 유일한 신호다.
                            // TODO(#274 Phase 3): TowerAsset.OnValidate로 저작 시점 검증을 되살린다.
                            Debug.LogError(
                                $"[Tower] Magic 타워인데 MagicEffectType이 None입니다 (TowerID={asset.TowerID}) — " +
                                "Buff/Debuff를 선택해야 오라가 동작합니다.", host);
                            break;
                    }
                    break;
            }
        }

        // 재조립(합성 결과 배치·풀 재사용)에서 컴포넌트가 중복되지 않도록 이미 있으면 재사용한다.
        static T GetOrAdd<T>(GameObject host) where T : MonoBehaviour, ITowerBehaviour
            => host.TryGetComponent(out T existing) ? existing : host.AddComponent<T>();
    }
}
