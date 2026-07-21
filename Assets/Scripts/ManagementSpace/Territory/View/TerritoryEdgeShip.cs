using UnityEngine;

/// <summary>
/// 영토 엣지(두 노드 사이) 위를 왕복하는 배 뷰 — 로직 없이 이동/방향만 담당한다(#93).<br/>
/// <see cref="TerritoryGraphView"/>가 공개된 엣지마다 배 프리팹을 하나씩 인스턴스화하고
/// <see cref="Init"/>로 경로/파라미터를 주입한다. 두 끝점 사이를 <c>speed</c>로 오가며(왕복)
/// 진행 방향을 바라본다.<br/>
/// 배 FBX의 forward 축이 확정되지 않으므로 <c>yawOffset</c>으로 뱃머리 방향을 보정한다.
/// </summary>
public class TerritoryEdgeShip : MonoBehaviour
{
    private Vector3 _a;
    private Vector3 _b;
    private float _speed;
    private float _yawOffset;
    private float _turnSpeed;

    private float _segLength;
    private float _t;       // A(0) → B(1) 정규화 진행도
    private int _dir = 1;   // +1: A→B, -1: B→A
    private bool _ready;

    /// <summary>
    /// 경로/파라미터 주입. endA/endB는 월드 좌표. endpointInset만큼 양끝을 안쪽으로 당겨
    /// 노드 메시와 겹침을 줄이고, heightOffset으로 수면 높이를 미세 조정한다.
    /// </summary>
    public void Init(Vector3 endA, Vector3 endB, float speed, float yawOffset,
        float heightOffset, float endpointInset, float turnSpeed)
    {
        var delta = endB - endA;
        float len = delta.magnitude;
        if (len > Mathf.Epsilon)
        {
            var unit = delta / len;
            // 경로가 사라지지 않도록 인셋을 세그먼트 절반 미만으로 클램프.
            float inset = Mathf.Min(Mathf.Max(0f, endpointInset), len * 0.45f);
            endA += unit * inset;
            endB -= unit * inset;
        }
        endA.y += heightOffset;
        endB.y += heightOffset;

        _a = endA;
        _b = endB;
        _speed = Mathf.Max(0f, speed);
        _yawOffset = yawOffset;
        _turnSpeed = Mathf.Max(0f, turnSpeed);
        _segLength = Vector3.Distance(_a, _b);
        _t = 0f;
        _dir = 1;
        _ready = _segLength > Mathf.Epsilon;

        transform.position = _a;
        var facing = _b - _a;
        if (facing.sqrMagnitude > Mathf.Epsilon)
        {
            transform.rotation = FacingRotation(facing);
        }
    }

    private void Update()
    {
        if (!_ready)
        {
            return;
        }

        _t += _dir * (_speed * Time.deltaTime) / _segLength;
        if (_t >= 1f)
        {
            _t = 1f;
            _dir = -1;
        }
        else if (_t <= 0f)
        {
            _t = 0f;
            _dir = 1;
        }
        transform.position = Vector3.Lerp(_a, _b, _t);

        // 진행 방향(왕복이라 끝점에서 반전)을 바라본다. 급회전을 피해 turnSpeed로 서서히 돌린다.
        var travel = (_b - _a) * _dir;
        if (travel.sqrMagnitude > Mathf.Epsilon)
        {
            var target = FacingRotation(travel);
            transform.rotation = _turnSpeed > 0f
                ? Quaternion.RotateTowards(transform.rotation, target, _turnSpeed * Time.deltaTime)
                : target;
        }
    }

    // XZ 평면 이동이므로 up은 월드 up. yawOffset으로 배 FBX의 뱃머리 축을 보정한다.
    private Quaternion FacingRotation(Vector3 forward)
    {
        return Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, _yawOffset, 0f);
    }
}
