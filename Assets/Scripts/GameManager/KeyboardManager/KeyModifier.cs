using System;

/// 단축키의 보조키 조합(#444). 판정 방식은 <see cref="KeyboardManager.Bind"/>의 `exactModifiers`가
/// 바인딩별로 고른다 — 기본값인 **정확히 일치**에서는 Ctrl+Shift+Z가 Ctrl+Z 바인딩을 발화시키지
/// 않는다. 되돌리기와 다시 실행을 같은 키에 나란히 둘 수 있어야 하기 때문이다(Redo는 아직 없다,
/// `CommandHistory` 주석).
///
/// 반대로 **보조키 없는 단축키**(<see cref="None"/>)는 정확 일치가 함정이 된다 — 이 게임에서 Shift는
/// 그룹 선택으로 쥔 채 조작하는 키라 "가끔 안 먹는" 증상이 된다(WL-201). 그런 바인딩은
/// `exactModifiers: false`로 등록한다(예: 배속 토글 스페이스바).
[Flags]
public enum KeyModifier
{
    None = 0,
    Ctrl = 1 << 0,
    Shift = 1 << 1,
    Alt = 1 << 2,
}
