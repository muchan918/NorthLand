using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;   // 프로젝트는 신규 Input System 사용
using UnityEngine.UI;

/// 단축키 입력의 단일 창구(#444). <see cref="MouseManager"/>의 형제 — 이쪽은 키보드만 본다.
///
/// **매니저는 무엇이 일어나야 하는지 모른다.** 기능 쪽이 자기 단축키를 등록하고(<see cref="Bind"/>),
/// 매니저는 눌림을 감지해 등록된 핸들러를 부를 뿐이다. 단축키가 늘어날 때 이 파일을 고치지 않는 것이
/// 이 설계의 목적이다 — 반대로 여기에 `if (Ctrl+Z) Undo()`를 쌓으면 매니저가 모든 시스템을 알게 된다
/// (`MouseManager`가 "생산 건물인가"를 판정하지 않는 것과 같은 원칙).
///
/// **바인딩 목록은 static이고, 펌프(이 MonoBehaviour)는 씬 배선 없이 스스로 뜬다.** 등록은 보통
/// `[RuntimeInitializeOnLoadMethod]`에서 일어나 펌프보다 먼저이므로, 목록이 인스턴스에 매달려 있으면
/// 등록 시점에 받을 곳이 없다. 자가 부팅은 `ResidentDragCoordinator`와 같은 이유이기도 하다 —
/// 정본 씬을 건드리지 않는다(`Docs/Core/SceneWorkflow.md`).
[DisallowMultipleComponent]
public class KeyboardManager : MonoBehaviour
{
    /// 등록된 단축키 한 줄. 핸들러 동등성으로 <see cref="Unbind"/>가 자기 것만 지운다.
    private readonly struct Binding
    {
        public readonly Key Key;
        public readonly KeyModifier Modifiers;
        public readonly Action Handler;
        public readonly string Label; // 로그용 이름("되돌리기"). 비워도 동작한다

        /// 눌린 보조키가 <see cref="Modifiers"/>와 **정확히** 같아야 발화하는가(기본 true).
        /// false면 "요구한 보조키가 모두 눌려 있기만 하면" 되고 나머지는 무시한다 — WL-201.
        public readonly bool ExactModifiers;

        public Binding(Key key, KeyModifier modifiers, Action handler, string label, bool exactModifiers)
        {
            Key = key;
            Modifiers = modifiers;
            Handler = handler;
            Label = label;
            ExactModifiers = exactModifiers;
        }

        // 동등성에 ExactModifiers는 넣지 않는다 — 같은 (키+보조키+핸들러)를 판정 방식만 바꿔 두 번
        // 등록하는 것은 실수이지 의도가 아니다. 먼저 등록된 쪽이 남고 뒤엣것은 조용히 버려진다.
        public bool Matches(Key key, KeyModifier modifiers, Action handler) =>
            Key == key && Modifiers == modifiers && Handler == handler;

        /// 지금 눌린 보조키 조합이 이 바인딩을 발화시키는가.
        public bool AcceptsModifiers(KeyModifier held) =>
            ExactModifiers ? held == Modifiers : (held & Modifiers) == Modifiers;

        /// 로그에 쓰는 이름. 라벨을 안 넘겼으면 키 조합으로 대신한다.
        public string Describe() =>
            !string.IsNullOrEmpty(Label) ? Label
            : Modifiers == KeyModifier.None ? Key.ToString()
            : $"{Modifiers}+{Key}";
    }

    // ⚠ 플레이 세션마다 비우지 **않는다.** 등록은 대부분 static 진입점에서 일어나는데, 그쪽도
    //    `[RuntimeInitializeOnLoadMethod]`라 여기서 비우면 **어느 쪽이 먼저 도는지 정해져 있지 않아**
    //    그 프레임에 등록된 것을 지워버릴 수 있다. 대신 Bind가 중복을 걸러 "도메인 리로드 없이 플레이"에서
    //    같은 바인딩이 두 번 쌓이는 것을 막는다.
    private static readonly List<Binding> s_bindings = new();

    public static KeyboardManager Instance { get; private set; }

    // 이번 프레임에 발화할 바인딩. 핸들러가 Bind/Unbind를 부를 수 있으므로 목록을 훑는 도중에 부르지
    // 않는다(순회 중 변경). 매 프레임 도는 경로라 버퍼는 재사용한다.
    private readonly List<Binding> _fired = new();

    /// 단축키를 등록한다. 같은 (키 + 보조키 + 핸들러)가 이미 있으면 아무 일도 하지 않는다.
    ///
    /// ⚠ 인스턴스 메서드를 넘겼다면 대상이 사라질 때 <see cref="Unbind"/>로 걷어야 한다 — 목록이
    /// static이라 파괴된 오브젝트를 붙들고 남는다. static 진입점(예: <see cref="UndoRequest.Submit"/>)은
    /// 앱 수명과 같으므로 해제할 필요가 없다.
    ///
    /// <param name="exactModifiers">
    /// 기본 true — 눌린 보조키가 <paramref name="modifiers"/>와 정확히 같아야 발화한다. Ctrl+Shift+Z가
    /// Ctrl+Z를 겸하지 않게 하는 판정이라 **조합키 단축키는 이 기본값을 쓴다.**
    ///
    /// 반대로 **보조키 없는 단축키**는 이 기본값이 함정이 된다: 이 프로젝트에서 Shift는 그룹 선택으로
    /// 쥔 채 조작하는 키라(`MouseManager.cs:353-354`) 무보조키 단축키가 "가끔 안 먹는다"가 되고,
    /// 원인이 이 한 줄이라는 것이 증상에서 멀다(WL-201). 그런 단축키는 false를 넘겨 "요구한 보조키만
    /// 눌려 있으면 나머지는 무시"로 등록한다.
    ///
    /// ⚠ 전역 정책으로 뒤집지 않고 바인딩별로 고르게 둔 이유는, 같은 키를 보조키로 갈라 쓰는 조합이
    /// 나중에 들어와도 서로를 조용히 겸하지 않게 하기 위함이다.
    /// </param>
    public static void Bind(Key key, KeyModifier modifiers, Action handler, string label = null,
        bool exactModifiers = true)
    {
        if (handler == null)
        {
            Debug.LogError($"[단축키] 핸들러가 null입니다: {modifiers}+{key}");
            return;
        }

        for (int i = 0; i < s_bindings.Count; i++)
        {
            if (s_bindings[i].Matches(key, modifiers, handler)) return;
        }

        s_bindings.Add(new Binding(key, modifiers, handler, label, exactModifiers));
    }

    /// <see cref="Bind"/>의 대칭짝. 등록되지 않은 조합이면 아무 일도 하지 않는다.
    public static void Unbind(Key key, KeyModifier modifiers, Action handler)
    {
        for (int i = s_bindings.Count - 1; i >= 0; i--)
        {
            if (s_bindings[i].Matches(key, modifiers, handler)) s_bindings.RemoveAt(i);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject(nameof(KeyboardManager));
        Instance = go.AddComponent<KeyboardManager>();
        DontDestroyOnLoad(go);
    }

    /// ⚠ **씬에 배치하지 말 것.** 펌프는 <see cref="Bootstrap"/>이 스스로 띄운다. 그래도 누군가 얹었을
    /// 때 단축키가 통째로 죽지 않도록, 씬 인스턴스도 여기서 `DontDestroyOnLoad`로 끌어올린다 —
    /// `Bootstrap`은 플레이 세션당 **1회만** 돌기 때문에, 씬 인스턴스가 `Instance`를 선점한 뒤 씬 전환에
    /// 파괴되면 다시 띄워 줄 사람이 없다(`MouseManager.Awake`와 같은 처리).
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // Keyboard.current는 키보드 없는 환경(모바일 빌드)에서 null이라 매 프레임 NRE가 된다
        // (`BalanceTestPanel`과 같은 방어).
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || s_bindings.Count == 0) return;

        // 텍스트를 입력하는 중이면 단축키를 삼킨다 — 이름 입력 중 Ctrl+Z가 게임 조작을 되돌리면
        // 되돌아간 것이 화면 어디에도 보이지 않는다.
        if (IsTypingInTextField()) return;

        KeyModifier held = ReadModifiers(keyboard);

        _fired.Clear();
        for (int i = 0; i < s_bindings.Count; i++)
        {
            Binding binding = s_bindings[i];

            // 보조키 판정은 바인딩이 정한다(Bind의 exactModifiers 주석). 기본은 **정확히 일치** —
            // 포함 판정이면 Ctrl+Shift+Z가 Ctrl+Z까지 함께 발화시켜, 나중에 붙는 조합키가 기존
            // 단축키를 조용히 겸하게 된다. 무보조키 단축키만 포함 판정을 골라 쓴다(WL-201).
            if (!binding.AcceptsModifiers(held)) continue;
            if (!keyboard[binding.Key].wasPressedThisFrame) continue;

            _fired.Add(binding);
        }

        for (int i = 0; i < _fired.Count; i++)
        {
            Binding fired = _fired[i];
            try
            {
                fired.Handler.Invoke();
            }
            catch (Exception e)
            {
                // 한 단축키의 예외가 나머지를 삼키지 않게 한다 — 같은 프레임에 발화한 다른 바인딩이
                // 조용히 건너뛰어지면 "가끔 안 먹는 단축키"가 된다.
                Debug.LogError($"[단축키] {fired.Describe()} 처리 중 예외가 발생했습니다: {e}");
            }
        }
    }

    private static KeyModifier ReadModifiers(Keyboard keyboard)
    {
        KeyModifier held = KeyModifier.None;
        if (keyboard.ctrlKey.isPressed) held |= KeyModifier.Ctrl;
        if (keyboard.shiftKey.isPressed) held |= KeyModifier.Shift;
        if (keyboard.altKey.isPressed) held |= KeyModifier.Alt;
        return held;
    }

    // 지금 포커스가 텍스트 입력 필드에 있는가. 매 프레임 도는 경로라 EventSystem 조회 1회 +
    // 컴포넌트 조회로만 끝낸다.
    private static bool IsTypingInTextField()
    {
        GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (focused == null) return false;

        if (focused.TryGetComponent(out TMP_InputField tmp)) return tmp.isFocused;
        if (focused.TryGetComponent(out InputField legacy)) return legacy.isFocused;
        return false;
    }
}
