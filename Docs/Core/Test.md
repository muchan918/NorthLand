# BalanceTestPanel 버튼 및 빌드 접근 정책

**BalanceTestPanel** 버튼 연결과 플레이어 빌드의 접근 정책을 기록한 문서. UI 표시 순서는 `Docs/Core/UIZOrder.md` §4(`UICanvas` 내부 sibling 7번)를 따른다.

> **상태**: 결정 완료(2026-09-03) — 플레이어 빌드에서 접근 차단

## 1. 결정 사항

- BalanceTestPanel 버튼 연결은 **Unity Editor 테스트 용도**로 유지한다.
- **F4**를 통한 패널 접근은 Unity Editor에서만 허용한다.
- Development Build를 포함한 모든 플레이어 빌드에서는 패널과 `BalanceTestPanel` 컴포넌트를 비활성화한다.

## 2. 유지 항목

- `PlayerBase.DebugInvincible`은 튜토리얼 시스템에서도 사용하므로 유지한다.
- `DebugRowButtonPrefab`은 Editor 전용 디버그 패널에서 사용하므로 유지한다.

## 3. 관련 문서

- `Docs/Core/UIZOrder.md` §4 — BalanceTestPanel의 UI 표시 순서 및 위치 규칙
