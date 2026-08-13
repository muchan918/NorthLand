using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 놓인 건물 인스턴스의 전역 목록(#341). <b>BuildingAsset(SO) → 그 건물이 서 있는 Transform</b> 한 방향만 푼다.<br/>
/// <br/>
/// <b>왜 필요한가.</b> <see cref="ManagementController"/>는 건물을 <b>SO로만</b> 안다 — 자원·레벨·배치 수는
/// 씬 좌표가 없어도 성립하기 때문이다. 그런데 "이 건물에서 주민이 걸어 나온다"(Resident.md §3.2)처럼
/// <b>월드 좌표가 필요한 소비처</b>가 생기면 SO만으로는 닿지 못한다. 그 간극을 여기서만 메운다.<br/>
/// <br/>
/// <see cref="ResidentDoorPointRegistry"/>·<see cref="ResidentWaypointRegistry"/>와 같은 방식이다 —
/// 대상이 스스로 등록/해제하고, 소비처는 매 질의마다 씬을 훑지 않는다(<c>FindObjectsByType</c> 금지).<br/>
/// <br/>
/// 등록 주체는 <see cref="BuildingInfo"/>다. 건물 루트에 이미 붙어 있고 자기 SO를 들고 있는 유일한
/// 컴포넌트라, 전용 마커를 새로 만들어 프리팹마다 붙이는 authoring 비용을 치를 이유가 없다.<br/>
/// <br/>
/// <b>SO를 키로 쓰는 것은 설계상 보장된 전제이지 편의가 아니다.</b> 생산 건물은 <b>나무꾼의 집·광산·농장
/// 3종 고정</b>이고(GDD §6.1), 「건물 건설」은 <i>"새 건물을 지어 할 수 있는 작업을 <b>해금</b>하는 시스템"</i>
/// (GDD §4 표)이라 <b>같은 건물을 여러 채 짓는 개념이 없다.</b> 그래서 "SO 하나 = 씬 인스턴스 하나"가
/// 성립하고, 아래 중복 경고는 <b>authoring 실수를 잡는 그물</b>이지 예상되는 경우가 아니다.<br/>
/// <br/>
/// ⚠ <b>이 전제가 바뀌면 여기만 고쳐서는 안 된다.</b> <see cref="ManagementController.LineIndexOf"/>가
/// 같은 전제 위에 있어서(SO로 라인을 찾는다) <b>둘을 함께</b> 인스턴스 기반으로 옮겨야 한다. 한쪽만
/// 옮기면 배치 −1 퇴장이 늘 첫 번째 건물에서만 나오고, 경고는 씬 로드 때 한 번 지나가 놓치기 쉽다(WL-021).
/// </summary>
public static class BuildingInstanceRegistry
{
    private static readonly Dictionary<BuildingAsset, Transform> _instances = new();

    /// <summary>
    /// 플레이 세션 시작마다 비운다. 도메인 리로드가 꺼져 있으면 이전 세션의 죽은 항목이 남는다
    /// (<see cref="ResidentDoorPointRegistry"/>와 같은 이유).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instances.Clear();
    }

    /// <summary>
    /// 건물 인스턴스를 등록한다. 같은 SO의 인스턴스가 둘 이상이면 <b>먼저 등록한 쪽</b>을 남기고 경고한다 —
    /// 생산 라인은 SO 하나당 건물 하나가 전제이고(<c>ManagementController.LineIndexOf</c>가 SO로 라인을 찾는다),
    /// 둘이 되면 "어느 건물에서 주민이 나오는가"가 조용히 갈린다.
    /// </summary>
    public static void Register(BuildingAsset building, Transform instance)
    {
        if (building == null || instance == null)
        {
            return;
        }

        if (_instances.TryGetValue(building, out Transform existing) && existing != null && existing != instance)
        {
            Debug.LogWarning($"[건물] '{building.BuildingID}'의 씬 인스턴스가 둘 이상입니다" +
                             $"({existing.name}, {instance.name}) — 먼저 등록된 쪽을 씁니다.", instance);
            return;
        }

        _instances[building] = instance;
    }

    /// <summary>등록을 해제한다. <b>자기가 등록한 인스턴스일 때만</b> 지운다 — 중복 건물이 꺼질 때
    /// 남의 등록을 지우면 살아 있는 건물이 목록에서 사라진다.</summary>
    public static void Unregister(BuildingAsset building, Transform instance)
    {
        if (building == null || instance == null)
        {
            return;
        }

        if (_instances.TryGetValue(building, out Transform existing) && existing == instance)
        {
            _instances.Remove(building);
        }
    }

    /// <summary>이 SO의 건물이 씬 어디에 서 있는가. 파괴된 항목(Unity 가짜 null)은 없는 것으로 본다.</summary>
    public static bool TryGet(BuildingAsset building, out Transform instance)
    {
        instance = null;

        if (building == null || !_instances.TryGetValue(building, out Transform found) || found == null)
        {
            return false;
        }

        instance = found;
        return true;
    }
}
