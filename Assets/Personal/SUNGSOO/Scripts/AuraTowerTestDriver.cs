using System.Reflection;
using UnityEngine;
using NorthLand.Combat;

namespace NorthLand.Sungsoo
{
    // 플레이 모드 스모크 테스트 드라이버(검증 전용).
    // 런타임에 debuff용 TowerAsset을 만들어 AuraTower에 물리고, "사거리 안 → 밖" 시나리오로
    // DoT 틱 발생 → 이탈 후 남은 지속시간 소진 → 만료를 Console 로그로 검증한다.
    // 빈 GameObject에 이 컴포넌트만 붙이면 자동 실행. (에셋 파일 없이 자기완결)
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
            asset.TowerType = TowerType.Magic;
            asset.MagicEffectType = MagicEffectType.Debuff;
            asset.Magic = new TowerAsset.MagicFields
            {
                BuffAura = new TowerAsset.BuffAuraFields(),
                DebuffAura = new TowerAsset.DebuffAuraFields
                {
                    Radius = 5f,
                    Interval = 0.3f,
                    Duration = 2f,
                    Damage = new OptionalDamage { HasDamage = true, DamageAmount = 5f, TickInterval = 0.3f },
                },
            };

            // 타워: 필드 세팅 후 활성화(OnEnable에서 루프 시작)
            var towerGo = new GameObject("TestAuraTower");
            towerGo.SetActive(false);
            var tower = towerGo.AddComponent<AuraTower>();
            SetPriv(tower, "data", asset);
            SetPriv(tower, "targetLayerMask", (LayerMask)(~0));   // 모든 레이어 감지(테스트)
            SetPriv(tower, "debugLog", true);
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
