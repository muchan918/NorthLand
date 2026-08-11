using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <see cref="ResidentNoStopZone"/>을 씬 뷰에서 직접 그린다(#332).
///
/// `NavMesh Obstacle`·`BoxCollider`와 **같은 면 핸들**이다 — 인스펙터에 숫자를 넣는 것이 아니라
/// 점을 잡아 끌어 통로에 맞춘다. 존은 아트 지형에 맞춰 그리는 것이라 씬을 보면서 조정하지 않으면
/// 저작이 성립하지 않는다(다리 상판 폭·골목 입구 위치를 수치로 알 방법이 없다).
///
/// <c>Handles.matrix</c>를 트랜스폼으로 잡는 것이 요점이다. 그래야 핸들이 **오브젝트의 회전을 따라가**
/// 비스듬한 골목에 상자를 맞출 수 있고, <see cref="ResidentNoStopZone.Contains"/>의 로컬 공간 판정과
/// 같은 좌표계에서 편집된다.
[CustomEditor(typeof(ResidentNoStopZone))]
public class ResidentNoStopZoneEditor : Editor
{
    private readonly BoxBoundsHandle handle = new BoxBoundsHandle();

    private void OnSceneGUI()
    {
        var zone = (ResidentNoStopZone)target;

        using (new Handles.DrawingScope(new Color(1f, 0.4f, 0.35f, 1f), zone.transform.localToWorldMatrix))
        {
            handle.center = zone.Center;
            handle.size = zone.Size;

            EditorGUI.BeginChangeCheck();
            handle.DrawHandle();

            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            // 이 기록이 없으면 핸들 조작이 Ctrl+Z로 되돌아가지 않고, 씬이 더티로 표시되지 않아
            // **저장 없이 씬을 닫으면 조용히 사라진다.**
            Undo.RecordObject(zone, "Edit Resident No Stop Zone");

            zone.SetBounds(handle.center, handle.size);
        }
    }
}
