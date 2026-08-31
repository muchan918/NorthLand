using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using NorthLand.Combat;

/// `[SerializeReference]` **단일 필드**에 타입 선택 UI를 붙인다.
///
/// Unity는 `List&lt;T&gt;`로 감싼 managed reference에는 `+` 버튼으로 타입을 고르게 해주지만
/// (`Tower.Actions`·`TowerAsset.Effects`가 그래서 멀쩡했다), **단일 필드에는 피커를 그리지 않는다.**
/// 그 결과 `TowerAsset.Attack.Flight`는 값만 보이고 종류를 바꿀 방법이 없었다 — 부품화의 목적이
/// "코드 0줄로 저작"인데 저작 UI가 없으면 반쪽이라 이 드로어로 메운다.
///
/// ⚠ **구 `TowerAssetEditor`의 부활이 아니다.** 그쪽은 `TowerType` enum별로 그릴 필드 그룹을
/// 하드코딩된 `FindProperty` 문자열로 골라서 #274 Phase 1에서 삭제됐다. 이 드로어는 **어떤
/// managed reference 필드에나 붙는 범용 도구**라 그 문제를 되살리지 않는다 — 기반 타입을
/// 런타임에 읽으므로 로직이 특정 타입을 알지 못한다.
///
/// **새 부품 축에 붙이는 법**: 아래 `ProjectileFlightDrawer`처럼 기반 타입만 지정한 빈 파생 클래스
/// 하나를 추가한다(`useForChildren: true`라 파생 타입에도 함께 적용된다).
public abstract class ManagedReferencePickerDrawer : PropertyDrawer
{
    const float PickerWidth = 150f;
    const string NoneLabel = "None";

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUI.GetPropertyHeight(property, label, true);

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ManagedReference)
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        var buttonRect = new Rect(
            position.x + position.width - PickerWidth,
            position.y,
            PickerWidth,
            EditorGUIUtility.singleLineHeight);

        // ⚠ 버튼을 PropertyField보다 **먼저** 그린다 — IMGUI는 그린 순서대로 마우스 이벤트를 주므로,
        // 순서가 곧 이벤트 우선권이다. 뒤에 그리면 겹치는 영역의 클릭을 먼저 그려진 PropertyField
        // (폴드아웃 행)가 가로채 버튼이 간헐적으로 안 눌린다. ManagedReference 헤더 행은 오른쪽
        // 값 칸에 아무것도 칠하지 않으므로 나중에 그려지는 PropertyField가 버튼을 덮지도 않는다.
        if (EditorGUI.DropdownButton(buttonRect, new GUIContent(CurrentTypeName(property)), FocusType.Keyboard))
            ShowMenu(property, buttonRect);

        // 기본 그리기(폴드아웃 + 자식 필드)는 그대로 쓴다.
        // 자식 필드를 직접 순회하지 않으므로 부품에 필드가 늘어도 이 드로어는 안 바뀐다.
        EditorGUI.PropertyField(position, property, label, true);
    }

    static string CurrentTypeName(SerializedProperty property)
    {
        string full = property.managedReferenceFullTypename;
        if (string.IsNullOrEmpty(full)) return NoneLabel;

        // "Assembly Namespace.Type" 형식 → 마지막 마디만 보여준다.
        int space = full.IndexOf(' ');
        string typeName = space >= 0 ? full.Substring(space + 1) : full;
        int dot = typeName.LastIndexOf('.');
        return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
    }

    void ShowMenu(SerializedProperty property, Rect buttonRect)
    {
        Type baseType = ResolveBaseType(property);
        var menu = new GenericMenu();

        menu.AddItem(new GUIContent(NoneLabel), string.IsNullOrEmpty(property.managedReferenceFullTypename),
                     () => Assign(property, null));

        if (baseType != null)
        {
            menu.AddSeparator("");
            foreach (Type t in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (t.IsAbstract || t.IsGenericTypeDefinition) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;   // 인스턴스를 만들 수 없으면 후보가 아니다

                Type captured = t;
                menu.AddItem(new GUIContent(t.Name), CurrentTypeName(property) == t.Name,
                             () => Assign(property, captured));
            }
        }

        menu.DropDown(buttonRect);
    }

    /// `managedReferenceFieldTypename`("Assembly Namespace.Type")에서 **런타임에** 기반 타입을 얻는다.
    /// 이 한 줄 덕분에 드로어가 특정 부품 축(비행·효과 등)을 알지 못한다.
    static Type ResolveBaseType(SerializedProperty property)
    {
        string full = property.managedReferenceFieldTypename;
        if (string.IsNullOrEmpty(full)) return null;

        int space = full.IndexOf(' ');
        if (space < 0) return null;

        string asm = full.Substring(0, space);
        string typeName = full.Substring(space + 1);
        return Type.GetType($"{typeName}, {asm}");
    }

    /// 타입을 교체하되 **같은 이름·호환 타입의 값을 이어받는다.**
    /// 없으면 `Homing ↔ Ballistic`을 오갈 때마다 Speed/ArcHeight를 다시 입력해야 하고,
    /// 그 재입력이 곧 저작 실수(캐논 곡사 높이를 빠뜨리는 식)가 된다.
    static void Assign(SerializedProperty property, Type type)
    {
        object previous = property.managedReferenceValue;
        object next = type == null ? null : Activator.CreateInstance(type);

        if (previous != null && next != null) CarryOverFields(previous, next);

        property.managedReferenceValue = next;
        property.serializedObject.ApplyModifiedProperties();
    }

    static void CarryOverFields(object from, object to)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        var source = new Dictionary<string, FieldInfo>();
        foreach (FieldInfo f in from.GetType().GetFields(flags)) source[f.Name] = f;

        foreach (FieldInfo target in to.GetType().GetFields(flags))
        {
            if (!source.TryGetValue(target.Name, out FieldInfo origin)) continue;
            if (!target.FieldType.IsAssignableFrom(origin.FieldType)) continue;

            target.SetValue(to, origin.GetValue(from));
        }
    }
}

/// 타워 행동 축(`Tower.Actions`)에 타입 피커를 붙인다.
/// 리스트 항목을 추가한 뒤 Attack/Beam/Aura/Ramp 행동을 선택해 프리팹에서 직접 조립한다.
[CustomPropertyDrawer(typeof(TowerAction), useForChildren: true)]
public sealed class TowerActionDrawer : ManagedReferencePickerDrawer { }

/// 투사체 비행 축(`TowerAsset.Attack.Flight`)에 타입 피커를 붙인다.
/// `useForChildren: true`라 `HomingFlight`/`BallisticFlight`가 담겨 있어도 이 드로어가 그린다.
[CustomPropertyDrawer(typeof(ProjectileFlight), useForChildren: true)]
public sealed class ProjectileFlightDrawer : ManagedReferencePickerDrawer { }

/// 명중 효과 축(`TowerAsset.Effects`)에 타입 피커를 붙인다.
///
/// ⚠ **이 클래스 상단 주석의 "List는 Unity가 알아서 해준다"는 전제가 실제로는 성립하지 않았다.**
/// `Effects` 리스트에서 `+`를 누르면 항목만 늘고 종류를 고를 드롭다운이 뜨지 않아, 저작 경로가
/// 사실상 막혀 있었다(기존 poison/choco/soda의 효과는 그 전에 저작된 것이다).
/// 단일 필드든 리스트 요소든 `SerializedPropertyType.ManagedReference`인 것은 같으므로
/// 기반 드로어가 그대로 동작한다 — `ResolveBaseType`이 리스트 요소에서는 요소 타입을 돌려준다.
[CustomPropertyDrawer(typeof(HitEffect), useForChildren: true)]
public sealed class HitEffectDrawer : ManagedReferencePickerDrawer { }

/// 조준 정책 축(`TowerAsset.Targeting`)에 타입 피커를 붙인다(#387).
///
/// 정책 클래스들은 수치 필드가 없어서 펼쳐도 비어 있다 — 이 축에서 저작하는 것은 **종류 하나**뿐이고,
/// 그 종류를 고르는 창구가 이 드롭다운이다.
[CustomPropertyDrawer(typeof(TargetingPolicy), useForChildren: true)]
public sealed class TargetingPolicyDrawer : ManagedReferencePickerDrawer { }

/// 튜토리얼 완료 조건 축(`TutorialStepAsset.completion`)에 타입 피커를 붙인다.
/// `useForChildren: true`라 어떤 파생 조건이 담겨 있어도 이 드로어가 그린다.
[CustomPropertyDrawer(typeof(TutorialCondition), useForChildren: true)]
public class TutorialConditionDrawer : ManagedReferencePickerDrawer { }