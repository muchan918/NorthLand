using System.Collections.Generic;
using UnityEngine;

// 튜토리얼이 강조할 UI에 붙이는 이름표.
// 스텝 에셋(ScriptableObject)은 씬 오브젝트를 참조할 수 없다 — 그래서 문자열 ID로 간접 참조한다.
// 절차적으로 생성되는 전투 타일은 이 방식으로 지목할 수 없다(그리드 좌표로 지목한다, TutorialStepAsset 참조).
[RequireComponent(typeof(RectTransform))]
public class TutorialAnchor : MonoBehaviour
{
    [SerializeField]
    private string id;

    // 씬을 뒤지지 않고 id로 바로 찾기 위한 등록부. 살아 있는 앵커만 들어 있다.
    private static readonly Dictionary<string, RectTransform> s_all = new Dictionary<string, RectTransform>();

    public static bool TryGet(string id, out RectTransform rect)
    {
        rect = null;

        return !string.IsNullOrWhiteSpace(id)
            && s_all.TryGetValue(id, out rect)
            && rect != null;
    }

    private void OnEnable()
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            Debug.LogWarning($"[{nameof(TutorialAnchor)}] id가 비어 있어 등록하지 않는다.", this);

            return;
        }

        if (s_all.TryGetValue(id, out RectTransform existing)
            && existing != null
            && existing != transform)
        {
            Debug.LogWarning($"[{nameof(TutorialAnchor)}] id '{id}'가 중복이다. 나중 것으로 덮는다.", this);
        }

        s_all[id] = (RectTransform)transform;
    }

    private void OnDisable()
    {
        // 자기 것일 때만 지운다 — id가 중복된 상황에서 남의 등록을 걷어내지 않게.
        if (!string.IsNullOrWhiteSpace(id)
            && s_all.TryGetValue(id, out RectTransform mine)
            && ReferenceEquals(mine, transform))
        {
            s_all.Remove(id);
        }
    }
}
