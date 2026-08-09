using UnityEngine;

/// <summary>
/// 카메라 줌(오쏘 사이즈)이 지정 범위 안에 있을 때만 켜지는 표시물의 공통 뼈대(#138).
/// 파생 클래스는 <see cref="ApplyVisible"/> 하나만 구현하면 된다.<br/>
/// <br/>
/// <b>왜 공용 <c>enum ZoomLevel</c>(Near/Mid/Far) 대신 컴포넌트별 범위인가</b>: 줌에 반응할 표시물은
/// 건물 힌트 파티클·머리 위 아이콘·주민 이모티콘처럼 성격이 제각각이고 <b>켜지는 구간이 서로 다르다</b>.
/// 공용 단계로 묶으면 경계를 공유하게 되어, 하나를 조정할 때마다 나머지가 함께 끌려간다.
/// 범위를 컴포넌트가 각자 들면 서로 간섭하지 않고 인스펙터에서 따로 튜닝된다.<br/>
/// <br/>
/// 베이스가 담당하는 것 — 파생이 다시 구현하지 않도록:
/// <list type="bullet">
/// <item>구독/해제와 <b>붙을 때 현재 값 1회 pull</b>(게임 도중 생성된 오브젝트도 즉시 올바른 상태로 시작)</item>
/// <item>멱등 — 상태가 그대로면 <see cref="ApplyVisible"/>을 부르지 않는다</item>
/// <item>비활성화 시 내려놓기 — 켜둔 채 꺼지면 표시물이 화면에 남는다</item>
/// </list>
/// <br/>
/// 히스테리시스는 두지 않는다. 줌이 <c>zoomSpeed</c> 단위로 뚝뚝 끊겨 움직여 경계에서 떨릴 일이
/// 구조적으로 없는데, 임계치만 둘로 늘어나 인스펙터가 어려워진다.<br/>
/// <br/>
/// 볼륨 페이드처럼 <b>연속값</b>이 필요한 소비처는 이 뼈대가 아니라
/// <see cref="CameraController2.OnZoomChanged"/>를 직접 구독한다.
/// </summary>
public abstract class ZoomDrivenVisibility : MonoBehaviour
{
    [Tooltip("표시를 유지할 오쏘 사이즈 하한(이 값 이상). 줌 아웃할수록 값이 커진다.")]
    [SerializeField] float _minOrthoSize = 120f;

    [Tooltip("표시를 유지할 오쏘 사이즈 상한(이 값 이하). 카메라의 Max Zoom Size보다 크게 두면 " +
             "'하한 이상 전부'와 같아진다 — 나중에 줌 범위를 넓혔을 때 조용히 잘리지 않는다.")]
    [SerializeField] float _maxOrthoSize = 999f;

    [Tooltip("줌을 읽어올 카메라 컨트롤러. 비우면 씬에서 찾는다.")]
    [SerializeField] CameraController2 _zoomSource;

    private bool _visible;

    /// <summary>현재 표시 상태. 파생이 다른 조건과 함께 판단해야 할 때 읽는다.</summary>
    protected bool IsVisible => _visible;

    /// <summary>표시 상태가 바뀔 때 호출된다(멱등 보장 — 같은 값으로 연달아 불리지 않는다).</summary>
    protected abstract void ApplyVisible(bool visible);

    protected virtual void OnEnable()
    {
        if (_zoomSource == null)
        {
            _zoomSource = FindFirstObjectByType<CameraController2>();
        }

        if (_zoomSource == null)
        {
            Debug.LogWarning($"[줌표시] {name}: CameraController2를 찾지 못해 줌에 반응하지 않습니다.", this);
            return;
        }

        _zoomSource.OnZoomChanged += HandleZoomChanged;

        // 이벤트는 "바뀔 때"만 온다 — 지금 상태는 여기서 직접 읽어야 한다.
        // force로 한 번 적용해 파생의 초기 상태(대개 '표시 안 함')를 실제 줌과 맞춘다.
        Evaluate(_zoomSource.CurrentZoomSize, force: true);
    }

    protected virtual void OnDisable()
    {
        if (_zoomSource != null)
        {
            _zoomSource.OnZoomChanged -= HandleZoomChanged;
        }

        // 켜둔 채로 꺼지면 표시물이 화면에 남는다.
        if (_visible)
        {
            _visible = false;
            ApplyVisible(false);
        }
    }

    protected virtual void OnValidate()
    {
        // 범위가 뒤집혀 있으면 어떤 줌에서도 만족하지 않아 "왜 안 나오지"가 된다 — 조용히 바로잡는다.
        if (_maxOrthoSize < _minOrthoSize)
        {
            _maxOrthoSize = _minOrthoSize;
        }
    }

    private void HandleZoomChanged(float orthoSize)
    {
        Evaluate(orthoSize, force: false);
    }

    private void Evaluate(float orthoSize, bool force)
    {
        bool next = orthoSize >= _minOrthoSize && orthoSize <= _maxOrthoSize;

        if (!force && next == _visible)
        {
            return;
        }

        _visible = next;
        ApplyVisible(next);
    }
}
