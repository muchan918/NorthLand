using System;

/// 단축키의 보조키 조합(#444). <see cref="KeyboardManager"/>가 **정확히 일치**로 판정하므로
/// Ctrl+Shift+Z는 Ctrl+Z 바인딩을 발화시키지 않는다 — 되돌리기와 다시 실행을 같은 키에 나란히 둘 수
/// 있어야 하기 때문이다(Redo는 아직 없다, `CommandHistory` 주석).
[Flags]
public enum KeyModifier
{
    None = 0,
    Ctrl = 1 << 0,
    Shift = 1 << 1,
    Alt = 1 << 2,
}
