using System;
using System.Collections.Generic;
using UnityEngine;

// 상체 패턴 레이어의 상태에 맞춰 파티클을 재생한다(가드 / 타워 봉인 / 소환 게이트 개방).
//
// **왜 BT 노드가 아니라 애니메이터 상태를 보는가.** VFX는 로직이 아니라 모션에 붙어야 한다.
// BT에서 쏘면 상태 전이(0.15~0.25초)보다 먼저 터져 팔이 올라가기 전에 이펙트가 보인다.
// 상태를 보고 재생하면 전이가 시작되는 프레임에 같이 시작하므로 항상 모션과 붙어 있다.
// 지속 이펙트(가드)도 상태를 벗어나는 것이 곧 정지라, 켜고 끄는 배선을 빠뜨릴 수가 없다.
// `BossUpperBodyLayer`가 레이어 weight에 대해 하는 일과 같은 구조다.
//
// **루프 여부로 정지 규칙이 갈린다.**
//  · 루프 이펙트(가드) — 상태를 벗어나면 `StopEmitting`으로 멈춘다. 이미 떠 있는 입자는
//    수명대로 사라지므로 뚝 끊기지 않는다.
//  · 1회성 이펙트(봉인 · 개방) — 상태를 벗어나도 **멈추지 않는다.** 상태보다 이펙트가 긴 것이
//    정상이기 때문이다(예: TowerSeal 상태는 약 1.2초인데 NovaWater는 약 5.6초).
//    여기서 멈추면 물결이 퍼지다 잘린다.
//
// **조명.** 이펙트에 딸린 `Light`는 파티클과 달리 "재생"이라는 개념이 없어 오브젝트가 살아 있는
// 동안 계속 켜져 있다. 가드 이펙트의 푸른 점광원이 보스 손에 영구히 붙어 있게 되므로, 파티클이
// 살아 있는 동안만 켠다. 판정은 `ParticleSystem.IsAlive(true)` 하나로 한다 — 방출이 멎은 뒤에도
// 남은 입자가 사라질 때까지 참이라, 루프 이펙트의 페이드아웃과 1회성 이펙트의 잔여 재생 모두
// 자동으로 덮인다.
//
// 파티클 프리팹은 `playOnAwake`를 꺼둬야 한다. 켜져 있으면 보스가 스폰되는 순간 전부 터진다.
public class BossPatternVfx : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        [Tooltip("이 상태가 있는 애니메이터 레이어. 상체 패턴(가드·봉인·소환)은 1, " +
                 "전신 모션(돌진 준비 등)은 0이다. 레이어를 틀리면 상태 이름이 맞아도 영영 매칭되지 않는다.")]
        public int layer = 1;

        [Tooltip("레이어의 상태 이름. 컨트롤러의 상태 이름과 정확히 같아야 한다.")]
        public string stateName;

        [Tooltip("그 상태에 들어갈 때 재생할 파티클. 루트 시스템을 넣는다(자식은 함께 재생된다).")]
        public ParticleSystem effect;

        [Tooltip("이 이펙트가 같이 켤 조명 중 **파티클 밖에 있는 것**. " +
                 "파티클 자식에 달린 조명은 자동으로 잡히므로 여기 넣지 않아도 된다. " +
                 "다른 이펙트에 딸린 조명을 빌려 쓸 때 지정한다(예: 소환·봉인이 왼손 조명을 함께 켠다).")]
        public Light[] extraLights;

        [NonSerialized] public int Hash;
        [NonSerialized] public bool Loops;
    }

    // 조명 하나와 그것을 켜는 파티클들. 여러 이펙트가 같은 조명을 공유할 수 있으므로
    // 조명 기준으로 뒤집어 들고 있는다 — 아래 「공유 조명」 참조.
    private class LightDriver
    {
        public Light Light;
        public ParticleSystem[] Sources;
    }

    // 감시 중인 레이어 하나의 상태. 레이어마다 독립적으로 흐르므로(전신 준비 모션과 상체 가드가
    // 동시에 성립한다) 진행 중인 이펙트도 레이어별로 들고 있어야 한다.
    private class LayerWatch
    {
        public int Layer;
        public int CurrentHash;
        public Entry Playing;
    }

    [SerializeField] private Animator animator;

    [SerializeField] private Entry[] entries;

    private LayerWatch[] watches;
    private LightDriver[] lightDrivers;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // 조용한 무동작을 막는다 — 참조가 빠지면 패턴은 도는데 이펙트만 안 뜬다.
        if (animator == null)
        {
            Debug.LogWarning($"[{name}] Animator를 찾지 못해 패턴 VFX를 끕니다.", this);
            enabled = false;
            return;
        }

        if (entries == null || entries.Length == 0)
        {
            Debug.LogWarning($"[{name}] 패턴 VFX 항목이 비어 있습니다.", this);
            enabled = false;
            return;
        }

        List<int> layers = new List<int>();

        foreach (Entry entry in entries)
        {
            entry.Hash = Animator.StringToHash(entry.stateName);

            // 레이어를 틀리면 상태 이름이 맞아도 영영 매칭되지 않는다 — 조용히 지나가면
            // "이펙트만 안 뜬다"로 보여 원인이 파티클 쪽에 있는 것처럼 오해하게 된다.
            if (entry.layer < 0 || entry.layer >= animator.layerCount)
            {
                Debug.LogWarning($"[{name}] 패턴 VFX '{entry.stateName}'의 레이어 {entry.layer}가 " +
                    $"AnimatorController에 없습니다(레이어 {animator.layerCount}개).", this);
                continue;
            }

            if (!layers.Contains(entry.layer))
            {
                layers.Add(entry.layer);
            }

            if (entry.effect == null)
            {
                Debug.LogWarning($"[{name}] 패턴 VFX '{entry.stateName}'에 파티클이 지정되지 않았습니다.", this);
                continue;
            }

            entry.Loops = entry.effect.main.loop;

            // playOnAwake가 켜져 있으면 이 Awake 시점에 이미 재생 중이다. 눈에 띄게 남긴다 —
            // 보스가 등장하자마자 패턴 이펙트가 전부 터지는 증상의 원인이 여기다.
            if (entry.effect.main.playOnAwake)
            {
                Debug.LogWarning($"[{name}] 파티클 '{entry.effect.name}'의 Play On Awake가 켜져 있습니다. " +
                    "스폰 즉시 재생되므로 프리팹에서 끄세요.", entry.effect);
            }

            entry.effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        BuildLightDrivers();

        watches = new LayerWatch[layers.Count];

        for (int i = 0; i < layers.Count; i++)
        {
            watches[i] = new LayerWatch { Layer = layers[i], CurrentHash = CurrentStateHash(layers[i]) };
        }
    }

    // 조명 → 그것을 켜는 파티클 목록으로 뒤집어 모은다.
    //
    // **공유 조명.** 한 조명을 여러 이펙트가 함께 쓸 수 있다(소환·봉인이 왼손 조명을 빌려 쓰는 구성).
    // 항목마다 따로 `Light.enabled`를 쓰면 같은 프레임에 한쪽은 켜고 한쪽은 꺼서 마지막 대입이
    // 이기는, 순서에 의존하는 깜빡임이 된다. 조명 기준으로 모아 **하나라도 살아 있으면 켠다**는
    // 논리합으로 판정하면 그 경합이 성립하지 않는다.
    private void BuildLightDrivers()
    {
        Dictionary<Light, List<ParticleSystem>> map = new Dictionary<Light, List<ParticleSystem>>();

        foreach (Entry entry in entries)
        {
            if (entry.effect == null)
            {
                continue;
            }

            // 파티클 자식에 달린 조명은 자동으로 잡는다. 인스펙터에서 지정한 것은 그 위에 더한다.
            foreach (Light light in entry.effect.GetComponentsInChildren<Light>(true))
            {
                Register(map, light, entry.effect);
            }

            if (entry.extraLights == null)
            {
                continue;
            }

            foreach (Light light in entry.extraLights)
            {
                if (light == null)
                {
                    Debug.LogWarning($"[{name}] 패턴 VFX '{entry.stateName}'의 추가 조명 칸이 비어 있습니다.", this);
                    continue;
                }

                Register(map, light, entry.effect);
            }
        }

        lightDrivers = new LightDriver[map.Count];

        int i = 0;

        foreach (KeyValuePair<Light, List<ParticleSystem>> pair in map)
        {
            pair.Key.enabled = false;
            lightDrivers[i++] = new LightDriver { Light = pair.Key, Sources = pair.Value.ToArray() };
        }
    }

    private static void Register(Dictionary<Light, List<ParticleSystem>> map, Light light, ParticleSystem source)
    {
        if (!map.TryGetValue(light, out List<ParticleSystem> sources))
        {
            sources = new List<ParticleSystem>();
            map[light] = sources;
        }

        if (!sources.Contains(source))
        {
            sources.Add(source);
        }
    }

    private void Update()
    {
        UpdateStateChange();
        UpdateLights();
    }

    // 레이어마다 독립적으로 판정한다 — 전신 레이어의 돌진 준비와 상체 레이어의 가드가
    // 동시에 성립할 수 있고, 한쪽의 상태 전환이 다른 쪽 이펙트를 건드려서는 안 된다.
    private void UpdateStateChange()
    {
        foreach (LayerWatch watch in watches)
        {
            int hash = CurrentStateHash(watch.Layer);

            if (hash == watch.CurrentHash)
            {
                continue;
            }

            watch.CurrentHash = hash;

            // 루프 이펙트만 거둔다. 1회성은 자기 수명대로 끝나야 한다(클래스 주석 참조).
            if (watch.Playing != null && watch.Playing.Loops && watch.Playing.effect != null)
            {
                watch.Playing.effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            watch.Playing = null;

            foreach (Entry entry in entries)
            {
                if (entry.layer != watch.Layer || entry.Hash != hash || entry.effect == null)
                {
                    continue;
                }

                entry.effect.Play(true);
                watch.Playing = entry;
                break;
            }
        }
    }

    // 파티클이 살아 있는 동안만 조명을 켠다.
    //
    // 상태 전환이 아니라 매 프레임 파티클 상태를 보는 이유: 1회성 이펙트는 상태를 벗어난 뒤에도
    // 계속 재생되므로(클래스 주석) 끄는 시점이 상태 전환과 일치하지 않는다. `IsAlive`는 방출이
    // 멎은 뒤 남은 입자가 사라질 때까지 참이라, 루프의 페이드아웃과 1회성의 잔여 재생을 한 판정으로
    // 덮는다.
    private void UpdateLights()
    {
        foreach (LightDriver driver in lightDrivers)
        {
            bool alive = false;

            // 하나라도 살아 있으면 켠다 — 공유 조명의 경합을 없애는 논리합(BuildLightDrivers 참조).
            foreach (ParticleSystem source in driver.Sources)
            {
                if (source != null && source.IsAlive(true))
                {
                    alive = true;
                    break;
                }
            }

            // 매 프레임 대입하지 않는다 — Light.enabled 쓰기는 렌더 파이프라인에 통지가 따른다.
            if (driver.Light.enabled != alive)
            {
                driver.Light.enabled = alive;
            }
        }
    }

    // 전이 중에는 목적지 상태를 본다 — 전이가 시작되는 프레임부터 이펙트가 같이 올라와야
    // 모션과 어긋나지 않는다(BossUpperBodyLayer의 weight 페이드와 같은 기준).
    private int CurrentStateHash(int layer)
    {
        AnimatorStateInfo info = animator.IsInTransition(layer)
            ? animator.GetNextAnimatorStateInfo(layer)
            : animator.GetCurrentAnimatorStateInfo(layer);

        return info.shortNameHash;
    }
}
