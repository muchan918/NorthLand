using System.Collections.Generic;
using UnityEngine;

/// 씬에 있는 <see cref="Resident"/>의 전역 목록(#276). 주민이 서로를 찾는 유일한 출처다.
///
/// <see cref="ResidentWaypointRegistry"/>와 같은 방식이다 — 대상이 스스로 등록/해제하고, 소비처는
/// 매 질의마다 씬을 훑지 않는다. 조우 판정은 주민 30명이 각자 돌리므로 FindObjectsByType이면
/// 탐색이 인원수의 제곱으로 돈다.
///
/// 물리 질의(<c>Physics.OverlapSphere</c>)를 쓰지 않는 이유: 주민은 앰비언트 캐릭터라 전용 레이어·콜라이더
/// 규약이 없고, 이 시스템에 이미 레지스트리 선례가 있다. 30명 선형 순회는 구간 경계에서만 돌아 무시할 수 있다.
public static class ResidentRegistry
{
    private static readonly List<Resident> _residents = new();

    /// 등록 순서대로의 전체 목록. 비활성 항목이 섞일 수 있으므로 소비처가 상태를 확인한다.
    public static IReadOnlyList<Resident> Residents => _residents;

    /// 플레이 세션 시작마다 비운다. 도메인 리로드가 꺼져 있으면 이전 세션의 죽은 항목이 남는다
    /// (<see cref="ResidentWaypointRegistry"/>와 같은 이유).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _residents.Clear();
    }

    public static void Register(Resident resident)
    {
        if (resident == null || _residents.Contains(resident))
        {
            return;
        }

        _residents.Add(resident);
    }

    public static void Unregister(Resident resident)
    {
        if (resident == null)
        {
            return;
        }

        _residents.Remove(resident);
    }

    /// 반경 안에 있는 다른 주민의 수. R5 춤이 "혼자인가"를 판정하는 데 쓴다.
    ///
    /// 대화 후보 여부(<see cref="Resident.IsAvailableForConversation"/>)를 보지 **않는다.**
    /// 이미 대화 중인 사람도 옆에 있으면 있는 것이고, 남이 보는 앞에서 혼자 춤추는 그림은 똑같이 어색하다.
    ///
    /// ⚠ **들려 있는 주민은 뺀다**(<see cref="Resident.IsCarried"/>). 그쪽은 몸이 화면에 남아 있어도
    /// 공중에 매달려 커서를 따라다니는 중이라 "옆에 있는 사람"이 아니다. 종전에는 들리면 곧 비활성이라
    /// 아래 <c>isActiveAndEnabled</c>가 저절로 걸러 줬는데, 연출이 들어오며 그 필터가 무력해졌다(§8.1).
    ///
    /// 이 검사가 없어도 지금 당장은 티가 안 난다 — 가림 방지 오프셋이 들린 주민을 수평으로 250유닛
    /// 밀어내기 때문이다. 하지만 그 값은 인스펙터에서 낮출 수 있고 **아래 거리 판정은 높이를 무시하므로**,
    /// 우연히 성립하는 안전을 계약으로 바꿔 둔다.
    public static int CountNearby(Resident self, float radius)
    {
        if (self == null || radius <= 0f)
        {
            return 0;
        }

        Vector3 origin = self.transform.position;
        float radiusSqr = radius * radius;
        int count = 0;

        for (int i = 0; i < _residents.Count; i++)
        {
            Resident other = _residents[i];

            if (other == null || other == self || !other.isActiveAndEnabled || other.IsCarried)
            {
                continue;
            }

            // 높이는 무시한다 — TryFindNearestCandidate와 같은 이유.
            Vector3 delta = other.transform.position - origin;
            delta.y = 0f;

            if (delta.sqrMagnitude <= radiusSqr)
            {
                count++;
            }
        }

        return count;
    }

    /// <paramref name="self"/>가 말을 걸 만한 가장 가까운 주민을 찾는다(§7.1 조우).
    ///
    /// 걸러내는 것:
    ///  · 자기 자신
    ///  · 이미 대화 중이거나 비활성인 주민 (<see cref="Resident.IsAvailableForConversation"/>)
    ///  · 쿨다운이 남은 상대 — 실패한 조우와 방금 끝난 대화가 여기 걸린다(§7.1 재진입 방지)
    ///
    /// **쿨다운은 한쪽 표만 본다.** 성립이든 실패든 기록을 양쪽에 남기므로(호출부 책임) 내 표만 보면 충분하다.
    /// 양쪽을 다 보게 하면 같은 사실이 두 곳에 있어야 하는지가 흐려진다.
    ///
    /// 가장 가까운 상대를 고르는 이유: 확률을 굴릴 대상이 매번 달라지면 "스쳐 지나가다 말을 걸었다"가
    /// 아니라 "멀리 있는 사람에게 갑자기 말을 걸었다"가 된다.
    public static bool TryFindNearestCandidate(Resident self, float radius, out Resident result)
    {
        result = null;

        if (self == null || radius <= 0f)
        {
            return false;
        }

        Vector3 origin = self.transform.position;
        float bestSqr = radius * radius;

        for (int i = 0; i < _residents.Count; i++)
        {
            Resident candidate = _residents[i];

            // 파괴된 항목(Unity 가짜 null)이 섞여 있을 수 있다 — OnDisable을 못 탄 경우의 방어.
            if (candidate == null || candidate == self || !candidate.IsAvailableForConversation)
            {
                continue;
            }

            if (!self.Encounters.IsReady(candidate))
            {
                continue;
            }

            // 높이 차이는 무시한다. 경영 공간은 사실상 평면이고, Y를 세면 계단·언덕에서 옆에 선 주민이
            // 반경 밖으로 밀려난다.
            Vector3 delta = candidate.transform.position - origin;
            delta.y = 0f;

            float sqr = delta.sqrMagnitude;

            if (sqr >= bestSqr)
            {
                continue;
            }

            bestSqr = sqr;
            result = candidate;
        }

        return result != null;
    }

    /// <paramref name="self"/>가 **끼어들 수 있는** 가장 가까운 대화를 찾는다(§7.1 진행 중 합류).
    ///
    /// <see cref="TryFindNearestCandidate"/>와 정반대의 필터다 — 저쪽은 "혼자인 사람"을 찾고,
    /// 이쪽은 "이미 대화 중인 사람"을 찾는다. 그래서 <see cref="Resident.IsAvailableForConversation"/>을
    /// 쓸 수 없다(대화 중이면 거짓이다).
    ///
    /// 쿨다운은 그대로 본다. 방금 헤어진 무리에 곧바로 다시 끼면 해산이 의미를 잃는다.
    public static bool TryFindNearestJoinable(Resident self, float radius, int maxParticipants,
        out ResidentConversation session)
    {
        session = null;

        if (self == null || radius <= 0f || self.Conversation != null)
        {
            return false;
        }

        Vector3 origin = self.transform.position;
        float bestSqr = radius * radius;

        for (int i = 0; i < _residents.Count; i++)
        {
            Resident member = _residents[i];

            if (member == null || member == self || !member.isActiveAndEnabled)
            {
                continue;
            }

            ResidentConversation candidate = member.Conversation;

            if (candidate == null || !candidate.CanAccept(maxParticipants))
            {
                continue;
            }

            if (!self.Encounters.IsReady(member))
            {
                continue;
            }

            // 높이는 무시한다 — TryFindNearestCandidate와 같은 이유.
            Vector3 delta = member.transform.position - origin;
            delta.y = 0f;

            float sqr = delta.sqrMagnitude;

            if (sqr >= bestSqr)
            {
                continue;
            }

            bestSqr = sqr;
            session = candidate;
        }

        return session != null;
    }
}
