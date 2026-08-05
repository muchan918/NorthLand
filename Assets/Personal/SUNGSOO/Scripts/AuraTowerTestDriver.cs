using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using NorthLand.Combat;

namespace NorthLand.Sungsoo
{
    // 플레이 모드 스모크 테스트 드라이버(검증 전용).
    // 런타임에 debuff용 TowerAsset을 만들어 Tower에 물리고, "사거리 안 → 밖" 시나리오로
    // DoT 틱 발생 → 이탈 후 남은 지속시간 소진 → 만료를 Console 로그로 검증한다.
    // 빈 GameObject에 이 컴포넌트만 붙이면 자동 실행. (에셋 파일 없이 자기완결)
    //
    // 검증 근거는 각 phase의 HP 값이다. 예전엔 AuraTower가 StatusEffectHandler.debugLog를 켜서
    // 틱마다 로그가 찍혔지만, 오라가 액션(DebuffAuraAction)으로 바뀌면서
    // 직렬화 필드로 그 스위치를 물려줄 자리가 없어졌다 — HP 델타로 같은 것을 확인한다.
    public class AuraTowerTestDriver : MonoBehaviour
    {
        const float InRangeSeconds = 2f;
        const float ObserveAfterLeaveSeconds = 4f;

        DebugDamageTarget dummy;
        float t;
        int phase;

        static void SetPriv(object o, string field, object value)
            => o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);

        void Start()
        {
            // 런타임 debuff TowerAsset (파일 없이 in-memory)
            var asset = ScriptableObject.CreateInstance<TowerAsset>();
            asset.TowerID = "test_debuff";
            // #274 Phase 1: Magic 래퍼가 풀리면서 오라 필드가 SO 최상위로 올라왔다.
            asset.BuffAura = new TowerAsset.BuffAuraFields();
            asset.DebuffAura = new TowerAsset.DebuffAuraFields
            {
                Radius = 5f,
                Interval = 0.3f,   // 재스캔 주기
            };

            // #274 Phase 4: 무엇을 거는지는 Effects가 정한다. 예전에는 DebuffAuraFields에
            // Duration/Damage가 수기 필드로 박혀 있었다.
            asset.Effects = new List<HitEffect>
            {
                new PoisonStatus { DamagePerTick = 5f, TickInterval = 0.3f, Duration = 2f },
            };

            // 타워: 필드 세팅 후 활성화(OnEnable이 직렬화된 data로 액션을 초기화한다)
            var towerGo = new GameObject("TestAuraTower");
            towerGo.SetActive(false);
            var tower = towerGo.AddComponent<Tower>();
            SetPriv(tower, "data", asset);
            SetPriv(tower, "enemyLayerMask", (LayerMask)(~0));   // 모든 레이어 감지(테스트)

            // #274 Phase 2: 액션은 프리팹이 정본이라 런타임 생성 타워는 직접 담아줘야 한다.
            // 예전에는 TowerBehaviourFactory가 SO의 TowerType을 보고 알아서 붙여줬다.
            SetPriv(tower, "actions", new List<TowerAction> { new DebuffAuraAction() });

            towerGo.SetActive(true);

            // 더미: 콜라이더 포함, 사거리 안(2,0,0)
            var dummyGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dummyGo.name = "DummyTarget";
            dummyGo.transform.position = new Vector3(2f, 0f, 0f);
            dummy = dummyGo.AddComponent<DebugDamageTarget>();

            Debug.Log("[TEST] phase0: 더미 사거리 IN (2,0,0) — DoT 틱 발생해야 함");
        }

        void Update()
        {
            t += Time.deltaTime;

            if (phase == 0 && t >= InRangeSeconds)
            {
                phase = 1;
                dummy.transform.position = new Vector3(100f, 0f, 0f);
                Debug.Log($"[TEST] phase1: 더미 사거리 OUT, HP={dummy.currentHp:F1} — 갱신 끊김, 남은시간만 소진하며 틱 계속돼야 함");
            }
            else if (phase == 1 && t >= InRangeSeconds + ObserveAfterLeaveSeconds)
            {
                phase = 2;
                Debug.Log($"[TEST] phase2: 최종 HP={dummy.currentHp:F1} (이 이후 틱 없어야 함) [TEST COMPLETE]");
                enabled = false;
            }
        }
    }
}
