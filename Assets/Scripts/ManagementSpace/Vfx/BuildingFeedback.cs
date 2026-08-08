using System;
using UnityEngine;

/// <summary>
/// 건물에 플레이어 행동이 반영됐을 때(업그레이드·주민 증축 등) 그 건물 자리에서 연출을 1회 재생한다.
/// <see cref="ManagementController.OnBuildingAction"/>을 구독해 자기 건물(<see cref="_building"/>)의 알림만
/// 골라 받고, 행동 종류에 매핑된 파티클을 처음부터 다시 재생한다. 건물 루트에 붙인다.<br/>
/// <br/>
/// <b>연출을 늘릴 때 코드를 고치지 않는 것</b>이 이 컴포넌트의 목적이다 — 새 파티클은 <see cref="_entries"/>에
/// 항목을 하나 늘리는 인스펙터 작업으로 끝나고, 아직 연출이 정해지지 않은 행동은 슬롯을 비워두면 조용히
/// 무시된다. 그래서 지금 매핑이 하나도 없는 건물(연금술사의 집)에도 미리 붙여둔다.<br/>
/// <br/>
/// 파티클 프리팹 규약: 오브젝트는 <b>켜둔 채</b> Looping·Play On Awake를 끈다. 비활성 오브젝트에는
/// <see cref="ParticleSystem.Play(bool)"/>가 먹지 않는다.
/// </summary>
public class BuildingFeedback : MonoBehaviour
{
    [Serializable]
    private struct Entry
    {
        [Tooltip("이 이펙트를 재생시킬 행동.")]
        public ManagementController.BuildingAction Action;

        [Tooltip("재생할 파티클(비워두면 그 행동은 무시된다). 하위 시스템까지 함께 재생되므로 루트만 지정한다.")]
        public ParticleSystem Effect;
    }

    [Tooltip("이 건물의 BuildingAsset. 컨트롤러 이벤트를 이 SO로 필터링한다.")]
    [SerializeField] BuildingAsset _building;

    [Tooltip("행동 → 재생할 이펙트 매핑. 같은 행동이 여러 줄이면 전부 재생된다.")]
    [SerializeField] Entry[] _entries;

    private ManagementController _controller;

    private void OnEnable()
    {
        // 씬 로드는 모든 오브젝트를 만든 뒤 OnEnable을 돌리므로 여기서 탐색해도 안전하다.
        // 컨트롤러는 씬에 하나뿐이라 인스펙터 배선 없이 탐색으로 잇는다(BuildingInfo가 패널 싱글톤을 잇는 것과 같은 계보).
        _controller = FindFirstObjectByType<ManagementController>();
        if (_controller == null)
        {
            Debug.LogWarning($"[건물연출] {name}: ManagementController가 씬에 없어 연출이 재생되지 않습니다.", this);
            return;
        }

        _controller.OnBuildingAction += HandleBuildingAction;
    }

    private void OnDisable()
    {
        // 구독할 때 잡아둔 인스턴스에서만 해제한다 — 그 사이 컨트롤러가 교체돼도 남의 구독을 끊지 않는다.
        if (_controller != null)
        {
            _controller.OnBuildingAction -= HandleBuildingAction;
            _controller = null;
        }
    }

    private void HandleBuildingAction(BuildingAsset building, ManagementController.BuildingAction action)
    {
        if (building == null || building != _building || _entries == null)
        {
            return;
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Action == action)
            {
                Play(_entries[i].Effect);
            }
        }
    }

    private void Play(ParticleSystem effect)
    {
        if (effect == null)
        {
            return; // 연출이 아직 정해지지 않은 행동 — 빈 슬롯은 정상 상태다.
        }

        // 위 규약대로면 항상 활성이다. 꺼져 있다는 건 세팅이 어긋났다는 뜻이라 조용히 넘기지 않고 알린다
        // (Play가 실패해도 예외가 없어서, 경고가 없으면 "왜 안 나오지"로 시간을 버린다).
        if (!effect.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"[건물연출] {name}: {effect.name}이 비활성이라 재생할 수 없습니다. " +
                             "오브젝트는 켜두고 Play On Awake만 끄세요.", this);
            return;
        }

        // 연속 업그레이드로 직전 재생이 남아 있으면 잔상이 겹친다 — 지우고 처음부터.
        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.Play(true); // withChildren — 중첩된 하위 시스템(twinkle·Smoke 등)까지 함께
    }
}
