using UnityEngine;

/// 바로가기 버튼이 카메라를 옮길 지점. 건물 위치에서 오프셋만큼 떨어진 곳이 화면 중앙이 된다.
public class BuildingFocusPoint : MonoBehaviour
{
    [SerializeField] private BuildingInfo building;

    // 카메라 타겟이 설 자리. 기울어진 카메라라 건물 위치와 다르다.
    [SerializeField] private Vector3 focusOffset;

    // 0이면 현재 줌을 유지한다.
    [SerializeField] private float zoomSize;

    public BuildingInfo Building => building;
    public Vector3 FocusPosition => transform.position + focusOffset;
    public float ZoomSize => zoomSize;

    private void Reset() => building = GetComponent<BuildingInfo>();

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, FocusPosition);
        Gizmos.DrawWireSphere(FocusPosition, 1f);
    }
}