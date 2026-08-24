using System;
using UnityEngine;

// 카메라를 충분히 조작해 봤으면 충족된다.
// 시간이 아니라 '누적 이동 거리·누적 줌 변화량'으로 판정한다 — 휠을 한 번 굴리거나
// 이동 키를 한 프레임 누른 것으로 통과되지 않게 하려는 것이 목적이다(#271의 타이머 금지 규약도 지킨다).
//
// 한 조작만 요구하려면 나머지 요구량을 0으로 둔다.
// 예) WASD 단계: PanSource=Keyboard, RequiredPanDistance=60, RequiredZoomChange=0
//     줌 아웃 단계: RequiredPanDistance=0, ZoomDirection=Out, RequiredZoomChange=40
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class CameraMovedCondition : TutorialCondition
{
    // 어떤 이동 조작을 인정할지. Any는 출처를 가리지 않는다.
    public enum PanFilter
    {
        Any,
        Keyboard,
        Drag,
        Minimap
    }

    // 어느 방향의 줌을 인정할지. 오쏘 사이즈가 커지면 화면이 넓어지므로 Out이다.
    public enum ZoomFilter
    {
        Any,
        Out,
        In
    }

    [Tooltip("어떤 이동 조작을 인정할지.")]
    [SerializeField]
    private PanFilter panSource = PanFilter.Any;

    [Tooltip("누적으로 이동해야 하는 월드 거리. 0이면 이동을 요구하지 않는다.")]
    [Min(0f)]
    [SerializeField]
    private float requiredPanDistance = 60f;

    [Tooltip("어느 방향의 줌을 인정할지. Out은 화면이 넓어지는 쪽이다.")]
    [SerializeField]
    private ZoomFilter zoomDirection = ZoomFilter.Any;

    [Tooltip("누적으로 변해야 하는 오쏘 사이즈 총량. 0이면 줌을 요구하지 않는다.")]
    [Min(0f)]
    [SerializeField]
    private float requiredZoomChange = 0f;

    private CameraController2 _camera;

    // 아래 셋은 모두 Begin에서 다시 잡는다 — 조건 객체는 에셋에 저장되므로 이전 플레이의 값이 남는다.
    private float _panAccumulated;
    private float _zoomAccumulated;
    private float _lastZoom;

    public override void Begin(TutorialContext context)
    {
        _camera = context.Camera;

        if (_camera == null)
        {
            Debug.LogWarning($"[{nameof(CameraMovedCondition)}] 씬에서 CameraController2를 찾지 못해 이 단계를 넘어갈 수 없다.");

            return;
        }

        _panAccumulated = 0f;
        _zoomAccumulated = 0f;

        // 구독 전에 현재 줌을 pull해 기준을 맞춘다(OnZoomChanged의 계약).
        _lastZoom = _camera.CurrentZoomSize;

        _camera.OnMoved += OnMoved;
        _camera.OnZoomChanged += OnZoomChanged;

        // 두 요구량이 모두 0이면 시작 즉시 충족이다.
        CheckSatisfied();
    }

    public override void End()
    {
        if (_camera != null)
        {
            _camera.OnMoved -= OnMoved;
            _camera.OnZoomChanged -= OnZoomChanged;
            _camera = null;
        }
    }

    private void OnMoved(CameraController2.MoveSource source, float distance)
    {
        if (!Accepts(source))
        {
            return;
        }

        _panAccumulated += distance;

        CheckSatisfied();
    }

    private bool Accepts(CameraController2.MoveSource source)
    {
        switch (panSource)
        {
            case PanFilter.Keyboard: return source == CameraController2.MoveSource.Keyboard;
            case PanFilter.Drag: return source == CameraController2.MoveSource.Drag;
            case PanFilter.Minimap: return source == CameraController2.MoveSource.Minimap;
            default: return true;
        }
    }

    private void OnZoomChanged(float orthographicSize)
    {
        float delta = orthographicSize - _lastZoom;
        _lastZoom = orthographicSize;

        // 방향을 지정한 단계에서는 반대 방향으로 굴린 양이 진행도를 채우지 않는다.
        if (zoomDirection == ZoomFilter.Out && delta <= 0f) return;
        if (zoomDirection == ZoomFilter.In && delta >= 0f) return;

        _zoomAccumulated += Mathf.Abs(delta);

        CheckSatisfied();
    }

    private void CheckSatisfied()
    {
        if (_panAccumulated >= requiredPanDistance && _zoomAccumulated >= requiredZoomChange)
        {
            Fire();
        }
    }
}
