using UnityEngine;
using NorthLand.Combat;

/// 배치된 타워에 붙어 "그룹 선택(합성 재료) 참여 가능"을 선언하는 마커(IGroupSelectable 구현).
/// Tower.cs(Combat 소유)를 건드리지 않으려고 별도 컴포넌트로 분리했다. TowerPlacer가 타워 배치 시
/// 런타임으로 부착하므로(AddComponent), 게임 흐름으로 배치된 모든 타워(합성 결과 포함)가 자동으로
/// 그룹 선택 대상이 된다. MouseManager는 이 마커 유무만 보고 대상 타입(타워)은 모른다(제네릭 유지).
[DisallowMultipleComponent]
public class TowerGroupSelectable : MonoBehaviour, IGroupSelectable
{
    // 플레이스홀더 하이라이트 색/크기(아트 TBD — TowerMerge.md §8.4). 런타임 부착이라 인스펙터 배선이 없어
    // 코드로 생성한다. 후속에서 아웃라인/링 연출로 교체.
    private static readonly Color k_HighlightColor = new(0.2f, 0.9f, 1f, 0.5f);
    private const float k_HighlightSize = 3.5f;

    private Tower _tower;
    private GameObject _highlight;
    private Material _highlightMat;

    public Tower Tower => _tower != null ? _tower : (_tower = GetComponent<Tower>());

    private void Awake() => _tower = GetComponent<Tower>();

    public void OnGroupSelected() => SetHighlight(true);
    public void OnGroupDeselected() => SetHighlight(false);

    private void SetHighlight(bool on)
    {
        if (on && _highlight == null) CreateHighlight();
        if (_highlight != null) _highlight.SetActive(on);
    }

    private void CreateHighlight()
    {
        _highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _highlight.name = "MergeSelectionHighlight";
        Destroy(_highlight.GetComponent<Collider>()); // 선택 레이캐스트 방해 금지
        _highlight.transform.SetParent(transform, false);
        _highlight.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        _highlight.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // XZ 바닥에 눕힘
        _highlight.transform.localScale = Vector3.one * k_HighlightSize;
        _highlightMat = new Material(Shader.Find("Sprites/Default")) { color = k_HighlightColor };
        _highlight.GetComponent<Renderer>().sharedMaterial = _highlightMat;
    }

    private void OnDestroy()
    {
        if (_highlightMat != null) Destroy(_highlightMat);
    }
}
