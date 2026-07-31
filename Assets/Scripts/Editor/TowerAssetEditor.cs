using UnityEditor;
using UnityEngine;
using NorthLand.Combat;

[CustomEditor(typeof(TowerAsset))]
public class TowerAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var towerTypeProp = serializedObject.FindProperty("TowerType");
        var magicEffectTypeProp = serializedObject.FindProperty("MagicEffectType");

        EditorGUILayout.PropertyField(serializedObject.FindProperty("TowerID"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("TowerPrefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("GhostPrefab"));
        EditorGUILayout.PropertyField(towerTypeProp);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("Cost"), true);
        EditorGUILayout.Space();

        // enumValueIndex는 enum 선언 순서상의 인덱스. BuildingAssetEditor와 동일한 주의사항:
        // TowerType/MagicEffectType이 명시적 값 없이 선언 순서 그대로라 (int)value와 일치하지만,
        // 선언 순서가 바뀌거나 명시적 값이 추가되면 더 이상 일치하지 않으니 주의.
        var type = (TowerType)towerTypeProp.enumValueIndex;

        // OnHitStunDuration은 단일 타격에서만 소비된다(Projectile.OnHit의 default 분기). Area는 스플래시
        // 경로라 도달하지 않고, Chain은 투사체를 거치지 않는다 — 체인의 CC 미지원은 확정된 결정이다(#252).
        // 값이 실제로 들어갔을 때만 경고한다: 항상 떠 있는 안내문은 읽히지 않고, 저작 실수만 잡으면 된다.
        TowerAsset.AttackFields attack = TowerBehaviourFactory.ResolveAttackFields((TowerAsset)target);
        if (type != TowerType.Single && attack != null && attack.OnHitStunDuration > 0f)
        {
            EditorGUILayout.HelpBox(
                "OnHitStunDuration은 단일 타격 타워에서만 동작합니다 — 이 타입에서는 무시됩니다.",
                MessageType.Warning);
        }

        switch (type)
        {
            case TowerType.Single:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Single"), true);
                break;
            case TowerType.Area:
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Area"), true);
                break;
            case TowerType.Chain:
                // 체인은 히트스캔이라 Attack 하위의 투사체 필드가 무의미하다. 잎 필드를 개별 렌더링해
                // 감추는 대신 안내로 해결한다 — ChainFields에 필드가 늘 때마다 에디터를 함께 고쳐야 하는
                // 유지보수 지점을 만들지 않기 위함(#252). MagicEffectType.None의 HelpBox와 같은 패턴.
                EditorGUILayout.HelpBox(
                    "체인 타워는 히트스캔(빔)입니다 — ProjectilePrefab / ProjectileSpeed는 비워두세요.\n" +
                    "빔 연출은 BeamPrefab을 사용하며, 비워두면 코드가 기본 빔을 생성합니다.",
                    MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("Chain"), true);
                break;
            case TowerType.Magic:
                EditorGUILayout.PropertyField(magicEffectTypeProp);
                EditorGUILayout.Space();

                var magicEffectType = (MagicEffectType)magicEffectTypeProp.enumValueIndex;
                switch (magicEffectType)
                {
                    case MagicEffectType.Buff:
                        EditorGUILayout.PropertyField(
                            serializedObject.FindProperty("Magic.BuffAura"), true);
                        break;
                    case MagicEffectType.Debuff:
                        EditorGUILayout.PropertyField(
                            serializedObject.FindProperty("Magic.DebuffAura"), true);
                        break;
                    case MagicEffectType.None:
                        EditorGUILayout.HelpBox(
                            "Magic 타입은 MagicEffectType을 Buff 또는 Debuff로 설정해야 합니다.",
                            MessageType.Warning);
                        break;
                }
                break;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
