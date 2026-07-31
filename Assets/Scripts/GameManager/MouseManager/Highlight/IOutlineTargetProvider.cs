using UnityEngine;

/// 아웃라인을 "이 컴포넌트가 붙은 GameObject"가 아니라 **다른 GameObject**에 걸어야 할 때 구현한다
/// (#213, Docs/Core/InteractionOutline.md §5.4).
///
/// 유일한 현재 용도는 영지 노드다: 확보 전에는 회오리(런타임 생성 평면 Quad — 헐 아웃라인을 씌우면
/// 소용돌이가 아니라 사각 테두리가 나와 부적합)이고, 확보 후에는 섬/산 인스턴스로 시각물이 갈린다.
/// 그래서 회오리 상태에서는 null을 돌려 아웃라인을 끈다(기존 색 하이라이트를 그대로 쓴다).
///
/// null 반환은 오류가 아니라 정상 경로 — "이 대상은 지금 아웃라인 없음"을 뜻한다.
public interface IOutlineTargetProvider
{
    GameObject OutlineTarget { get; }
}
