using Unity.Behavior;
using Unity.Properties;
using UnityEngine;

// 지금이 밤인가(#276, R8 귀가).
//
// **용도가 하나다: Priority Abort의 감시 조건.** 참이 되는 순간 아래의 모든 브랜치(대화 · 춤 · 조우 · 산책)가
// 중단되고 Selector가 처음부터 재평가된다 — 주민 30명이 각자 이동 구간이 끝나기를 기다리지 않고
// **같은 틱에 일제히 집으로 향한다.**
//
// 선점 없이 하면 반응이 개체마다 0~4초씩 어긋나 "해가 지자 마을이 정리된다"가 아니라
// 뿔뿔이 반응하는 그림이 된다. 이 브랜치가 Priority Abort를 도입한 원래 근거다(§11.3).
//
// DayNightManager가 없으면 **낮으로 본다.** 주민 테스트 씬처럼 밤낮 시스템이 없는 곳에서
// 주민이 시작하자마자 사라지지 않게 하기 위해서다.
//
// 네임스페이스를 두지 않는다.
[System.Serializable, GeneratePropertyBag]
[Condition(
    name: "Resident Is Night",
    description: "DayNightManager의 현재 페이즈가 밤이면 참. Priority Abort의 감시 조건으로 쓴다.",
    story: "It is night",
    category: "Conditions/Resident",
    id: "a04aa977349043fc9c94f1ee6564720c")]
public partial class ResidentIsNightCondition : Condition
{
    public override bool IsTrue()
    {
        DayNightManager dayNight = DayNightManager.Instance;

        return dayNight != null && dayNight.CurrentPhase == DayNightManager.Phase.Night;
    }
}
