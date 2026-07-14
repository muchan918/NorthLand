using UnityEngine;

/// <summary>
/// 뷰 시각 검증용 하네스 (씬: TerritoryNodeTest.unity) — 입력 통합 전에도
/// 확장이 눈에 보이도록 일정 간격마다 프론티어 노드 하나를 자동 확보한다.<br/>
/// 프론티어가 소진되면 스스로 멈춘다. 입력 통합 후에는 씬에서 비활성화해 두고
/// "입력 없이도 모델·뷰가 완결 동작"하는 디그레이드 검증용으로 유지한다.
/// </summary>
public class TerritoryAutoClaimTest : MonoBehaviour
{
    [Tooltip("영토 컨트롤러(모델 소유자)")]
    [SerializeField] TerritoryController _controller;

    [Tooltip("자동 확보 간격(초)")]
    [SerializeField] float _interval = 2f;

    private float _nextTime;

    private void Start()
    {
        if (_controller == null)
        {
            Debug.LogError("[TerritoryTest] 컨트롤러가 할당되지 않아 자동 확보를 중단합니다.", this);
            enabled = false;
            return;
        }

        // 첫 확보 전에 초기 상태(본진+프론티어만 공개)를 한 박자 보여준다.
        _nextTime = Time.time + _interval;
    }

    private void Update()
    {
        if (_controller.Graph == null || Time.time < _nextTime)
        {
            return;
        }

        _nextTime = Time.time + _interval;

        TerritoryNode target = null;
        foreach (var node in _controller.Graph.Frontier)
        {
            target = node;
            break;
        }

        if (target == null)
        {
            Debug.Log($"[TerritoryTest] 프론티어 소진 — 자동 확장 종료 (보유 {_controller.Graph.OwnedCount}개)");
            enabled = false;
            return;
        }

        if (_controller.TryClaim(target.Id))
        {
            Debug.Log($"[TerritoryTest] 자동 확보: 노드 {target.Id}");
        }
    }
}
