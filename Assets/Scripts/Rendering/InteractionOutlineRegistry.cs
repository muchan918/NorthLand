using System.Collections.Generic;
using UnityEngine;

/// 스크린 스페이스 실루엣 아웃라인의 대상 등록소(#213, Docs/Core/InteractionOutline.md §3).
///
/// 셸 방식은 대상 렌더러마다 자식 렌더러를 만들어 부풀렸다. 이 방식은 아무것도 만들지 않는다 —
/// "이 렌더러들을 이 슬롯으로 마스크에 그려라"만 등록하고, 렌더러 피처가 그걸 소비한다.
/// 그래서 렌더러가 몇 개든 오브젝트 전체에 실루엣 하나가 나오고, 512개 상한도 사라진다.
///
/// 왜 renderingLayerMask 필터가 아니라 명시적 등록인가(§3.2): `FilteringSettings.renderingLayerMask`가
/// URP 17 Render Graph 경로에서 의도대로 도는지 미검증이라, 폴백으로 지정된
/// "수집해둔 렌더러 배열을 직접 그리는" 방식을 택했다. 대상 수가 적어(그룹당 1~5개) 부담이 없고
/// 필터 거동에 의존하지 않아 완전히 결정적이다.
public static class InteractionOutlineRegistry
{
    /// 마스크에 기록하는 슬롯. 값이 큰 쪽이 겹칠 때 우선한다(합성 셰이더의 SlotToColor와 순서 일치).
    public enum Slot
    {
        Hover = 1,
        Selected = 2,
        MergePreview = 3,
    }

    private sealed class Entry
    {
        public Slot Slot;
        public readonly List<Renderer> Renderers = new List<Renderer>();
    }

    // 키는 등록 주체(OutlineHighlight 등). 오브젝트가 파괴되면 키가 null이 되므로 소비 시점에 정리한다.
    private static readonly Dictionary<Object, Entry> s_entries = new Dictionary<Object, Entry>();

    private static readonly List<Object> s_deadKeys = new List<Object>();

    /// 등록된 대상이 하나도 없으면 렌더러 피처가 패스 전체를 건너뛴다(평시 비용 0).
    public static bool HasTargets => s_entries.Count > 0;

    /// <summary>
    /// owner가 소유한 렌더러들을 slot으로 등록한다. 같은 owner로 다시 부르면 교체된다.
    /// renderers가 비어 있으면 해제와 같다.
    /// </summary>
    public static void Set(Object owner, IReadOnlyList<Renderer> renderers, Slot slot)
    {
        if (owner == null)
        {
            return;
        }

        if (renderers == null || renderers.Count == 0)
        {
            Clear(owner);
            return;
        }

        if (!s_entries.TryGetValue(owner, out Entry entry))
        {
            entry = new Entry();
            s_entries.Add(owner, entry);
        }

        entry.Slot = slot;
        entry.Renderers.Clear();

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer r = renderers[i];

            // 파괴된 렌더러를 넘기면 DrawRenderer에서 터진다. 등록 시점에 걸러낸다.
            if (r != null)
            {
                entry.Renderers.Add(r);
            }
        }

        if (entry.Renderers.Count == 0)
        {
            Clear(owner);
        }
    }

    public static void Clear(Object owner)
    {
        if (owner == null)
        {
            return;
        }

        s_entries.Remove(owner);
    }

    /// 씬 전환 등으로 통째로 비울 때. 등록 주체가 사라졌는데 남아 있으면 마스크에 유령이 남는다.
    public static void ClearAll()
    {
        s_entries.Clear();
    }

    /// <summary>
    /// 렌더러 피처가 그릴 목록을 채운다. 죽은 키·렌더러는 이 시점에 정리한다 —
    /// 등록 주체가 OnDestroy에서 해제하지 못하고 사라지는 경우(씬 언로드)가 있다.
    /// </summary>
    public static void Collect(List<Renderer> hover, List<Renderer> selected, List<Renderer> mergePreview)
    {
        s_deadKeys.Clear();

        foreach (KeyValuePair<Object, Entry> pair in s_entries)
        {
            if (pair.Key == null)
            {
                s_deadKeys.Add(pair.Key);
                continue;
            }

            List<Renderer> into = pair.Value.Slot switch
            {
                Slot.MergePreview => mergePreview,
                Slot.Selected => selected,
                _ => hover,
            };

            List<Renderer> source = pair.Value.Renderers;

            for (int i = source.Count - 1; i >= 0; i--)
            {
                if (source[i] == null)
                {
                    source.RemoveAt(i);
                    continue;
                }

                into.Add(source[i]);
            }
        }

        for (int i = 0; i < s_deadKeys.Count; i++)
        {
            s_entries.Remove(s_deadKeys[i]);
        }

        s_deadKeys.Clear();
    }
}
