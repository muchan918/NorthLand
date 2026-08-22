using UnityEngine;
using UnityEngine.InputSystem;

/// "되돌려라" 요청 하나를 해석하는 단일 진입점(#444). 되돌리기 버튼과 Ctrl+Z가 **같은 이 경로**를 쓴다.
///
/// **왜 버튼 밖으로 뺐는가**: 규칙이 버튼의 `onClick` 안에 있는 동안은 (ⓐ) 단축키가 그 버튼이 씬에 있는지에
/// 의존하고 (ⓑ) "고스트를 들고 있으면 그것부터 치운다" 같은 규칙이 두 벌로 갈라진다. 요청 해석은 표시와
/// 무관한 규칙이라 히스토리 옆(`Assets/Scripts/Command/`)이 제자리다.
///
/// 밤·빈 스택 판정은 여기서 하지 않는다 — `CommandHistory.Undo`가 `CanUndo`로 이미 막는다. 같은 조건을
/// 두 곳에 두면 조용히 어긋난다.
///
/// 선택 해제도 여기서 하지 않는다. 무조건 풀면 방금 올린 건물의 패널이 닫혀 **되돌아간 레벨을 볼 수 없다** —
/// 파괴되는 대상을 아는 커맨드가 자기 몫으로 푼다(`TowerPlaceCommand.OnUndo`,
/// `ResidentSelectionCoordinator`의 "주민이었을 때만 푼다"와 같은 판단).
public static class UndoRequest
{
    /// Ctrl+Z를 이 진입점에 묶는다. **단축키를 원하는 쪽이 자기 단축키를 등록한다** — 그래야
    /// <see cref="KeyboardManager"/>가 되돌리기(그리고 앞으로 늘어날 모든 기능)를 몰라도 된다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterShortcut()
    {
        KeyboardManager.Bind(Key.Z, KeyModifier.Ctrl, Submit, "되돌리기");
    }

    /// 되돌리기 요청 1회.
    public static void Submit()
    {
        // 고스트를 들고 있으면 그것부터 치우고 끝낸다. 배치하려다 만 상태에서 엉뚱한 이전 조작이 되감기면
        // "무엇을 되돌렸는지"가 화면에서 읽히지 않는다 — 한 번의 요청은 한 가지만 한다. 진짜로 되돌리려면
        // 한 번 더 요청한다.
        if (MouseManager.Instance != null && MouseManager.Instance.IsPlacing)
        {
            MouseManager.Instance.CancelPlacement();
            return;
        }

        CommandHistory.Undo();
    }
}
