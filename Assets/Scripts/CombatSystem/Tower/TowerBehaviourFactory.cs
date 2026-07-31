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
        // TowerType에 맞는 공통 공격 스탯. Magic(또는 asset 미할당)은 공격 스탯이 없어 null.
        // 이 해석이 필요한 곳이 여럿이라(행동 조립·툴팁·정보 패널) 단일 출처로 둔다(WL-079).
        public static TowerAsset.AttackFields ResolveAttackFields(TowerAsset asset)
        {
            if (asset == null) return null;

            return asset.TowerType switch
            {
                TowerType.Single => asset.Single?.Attack,
                TowerType.Area => asset.Area?.Attack,
                TowerType.Chain => asset.Chain?.Attack,
                _ => null,
            };
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
                    // 공격 스탯이 비어 있으면 행동을 붙이지 않는다 — 무동작 컴포넌트를 남기지 않고
                    // "이 타워는 공격하지 않는다"가 Has<IAttackBehaviour>() 한 번으로 드러나게 한다.
                    if (ResolveAttackFields(asset) != null)
                    {
                        result.Add(GetOrAdd<AttackBehaviour>(host));
                    }
                    break;

                case TowerType.Chain:
                    // 체인만 전달 방식이 다르다(히트스캔). 발사와 명중이 같은 프레임에 끝나 "비행 중 첫 대상
                    // 사망"으로 홉 전체를 잃는 경로가 사라진다(#252). 명중 규칙은 ChainResolver를 공유하므로
                    // 투사체 경로와 체인 계산이 갈리지 않는다.
                    if (ResolveAttackFields(asset) != null)
                    {
                        result.Add(GetOrAdd<HitscanAttackBehaviour>(host));
                    }
                    break;

                case TowerType.Magic:
                    // Buff/Debuff는 서로 다른 행동이다. 예전에는 한 컴포넌트가 MagicEffectType으로
                    // 6곳에서 분기했는데, 그 분기가 전부 이 한 줄로 접힌다.
                    switch (asset.MagicEffectType)
                    {
                        case MagicEffectType.Buff:
                            if (asset.Magic?.BuffAura != null) result.Add(GetOrAdd<BuffAuraBehaviour>(host));
                            break;
                        case MagicEffectType.Debuff:
                            if (asset.Magic?.DebuffAura != null) result.Add(GetOrAdd<DebuffAuraBehaviour>(host));
                            break;
                        default:
                            // MagicEffectType 미선택은 SO 저작 실수다. TowerAssetEditor의 HelpBox가 이미
                            // 경고하지만, 그 경로를 안 거친 에셋도 있으므로 여기서 한 번 더 드러낸다.
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
