using System.Collections.Generic;
using UnityEngine;

/// 그룹 선택 후보(IGroupSelectable)의 전역 목록. 드래그 사각형 선택(#261)이 "화면 영역 안에 무엇이 있나"를
/// 묻는 유일한 출처다.
///
/// 왜 레지스트리인가: 레이캐스트는 **점 하나**만 쏠 수 있어 면적 판정에 쓸 수 없다. 사각형 선택은 후보를
/// 순회하며 월드 좌표를 스크린으로 투영해 포함 여부를 보는 수밖에 없는데, 그 후보 목록을 매 프레임
/// FindObjectsByType으로 긁으면 드래그 내내 씬 전체 탐색이 돈다 → 대상이 스스로 등록/해제한다.
///
/// 도메인 중립: 여기 담기는 것은 마커(IGroupSelectable)와 투영 기준점(Transform)뿐이다. "타워인가 주민인가"는
/// 소비처(MouseManager → 각 코디네이터)가 해석하므로, 주민 시스템이 들어와도 이 파일은 수정되지 않는다.
public static class GroupSelectableRegistry
{
    /// 마커 + 투영 기준점 한 쌍. IGroupSelectable은 좌표를 노출하지 않으므로(도메인 중립 유지)
    /// 등록 시 Transform을 함께 받는다.
    public readonly struct Entry
    {
        public readonly IGroupSelectable Target;
        public readonly Transform Anchor;

        public Entry(IGroupSelectable target, Transform anchor)
        {
            Target = target;
            Anchor = anchor;
        }
    }

    private static readonly List<Entry> _entries = new();

    /// 등록 순서대로의 후보 목록. **선택 순서와는 무관하다** — 사각형에 들어온 순서는 소비처가 따로 쌓는다.
    /// 순회 중 Anchor가 파괴된 항목(Unity 가짜 null)이 있을 수 있으므로 소비처가 건너뛴다.
    public static IReadOnlyList<Entry> Entries => _entries;

    /// 활성화 시 등록(OnEnable). 중복 등록은 무시한다.
    public static void Register(IGroupSelectable target, Transform anchor)
    {
        if (target == null || anchor == null) return;

        for (int i = 0; i < _entries.Count; i++)
        {
            if (ReferenceEquals(_entries[i].Target, target)) return;
        }

        _entries.Add(new Entry(target, anchor));
    }

    /// 비활성화·파괴 시 해제(OnDisable). Unity는 파괴 시에도 OnDisable을 부르므로 이 한 쌍만으로 누수가 없다.
    public static void Unregister(IGroupSelectable target)
    {
        if (target == null) return;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_entries[i].Target, target)) continue;
            _entries.RemoveAt(i);
            return;
        }
    }
}
