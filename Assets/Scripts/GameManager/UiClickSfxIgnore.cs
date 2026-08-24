using UnityEngine;

/// <summary>
/// 이 오브젝트(와 그 자식)의 버튼에는 <see cref="UiClickSfx"/>의 공용 클릭음을 내지 않는다.
///
/// 전역 훅은 배선을 잊을 수 없다는 것이 장점이지만, 자기 소리를 따로 내는 버튼에는 **빠져나갈 구멍이
/// 필요하다** — 없으면 그런 버튼이 생길 때마다 전역 훅 쪽에 예외 조건이 하나씩 박히게 된다.
///
/// 버튼 하나에 붙이면 그 버튼만, 패널 루트에 붙이면 그 안의 버튼 전부가 제외된다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("NorthLand/Audio/UI Click Sfx Ignore")]
public class UiClickSfxIgnore : MonoBehaviour
{
    /// <summary>
    /// 이 버튼을 공용 클릭음에서 제외한다. 없으면 붙이고, 이미 있으면 아무 일도 하지 않는다.
    ///
    /// **인스펙터가 아니라 코드에서 붙이는 이유**: 프리팹·씬을 건드리지 않아 병합 충돌이 늘지 않고
    /// (`TowerMergePanelView`가 호버 컴포넌트를 런타임 부착하는 것과 같은 선례), 무엇보다
    /// **"이 버튼은 자기 소리를 낸다"는 사실이 그 소리를 배선한 코드 바로 옆에 남는다.**
    /// 인스펙터에 흩어두면 소리를 떼어낼 때 제외 표시만 남아 원인을 찾기 어려워진다.
    ///
    /// 런타임 생성 버튼에도 그대로 쓸 수 있다.
    /// </summary>
    public static void ApplyTo(Component target)
    {
        if (target == null || target.GetComponent<UiClickSfxIgnore>() != null)
        {
            return;
        }

        target.gameObject.AddComponent<UiClickSfxIgnore>();
    }
}
