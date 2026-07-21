# TerritoryGraph — 경영 영토 확장 (그래프 노드 선택) [설계]

> **담당**: n0wst4ndup
> **이슈**: #TBD
> **경로(코드)**: `Assets/Scripts/ManagementSpace/Territory`
> **상태**: ✅ **씬 통합 완료(이슈 #67)** — `TerritoryController`/`TerritoryGraphView`가 실제
> `Assets/Scenes/GameScene.unity`에 배치돼 그래프 생성·확보(`ISelectable`)·호버 하이라이트
> (`IHoverable`)까지 플레이 가능. ✅ **노드 비주얼 에셋 적용(#127, PR#128, muchan)** — 임시 색상
> 구체를 상태별 비주얼(소용돌이/산)로 교체, §7.1 참고. ✅ **엣지 배 연출 적용(#93, muchan)** —
> 엣지 선(LineRenderer)을 배(SweetBoat) 왕복 연출로 교체, §7.2 참고. 효과 카탈로그(§5)·보상 수치(§8) 등
> 일부는 여전히 미착수 — 남은 TBD는 §8에서 계속 추적한다.
> 확정되지 않은 항목은 본문에서 **TBD / TODO**로 명시한다(docs-are-dev-reference 규약).
> **GDD 근거**: §4.1(두 공간) · §5.1(낮 경영—영토 확장) · §6.3(경영 영토 확장) ·
> §4.2(마나석=영토 확장 보상) · §6.1(주민 획득) · §3(Slay the Spire 노드 선택 응용) · §7(랜덤 리플레이)

이 문서는 경영 공간의 **영토 확장 시스템이 무엇을 하고, 어떤 경계로 다른 시스템과 만나는지**의 설계 기준선이다.
전투 공간의 그리드 맵(BattleMapBuilder)과 달리 **정형화되지 않은 그래프**로 확장한다(GDD §4.1 독립 이중 확장).

---

## 1. 목적 · GDD 의도

본진에서 시작해 인접 영토를 하나씩 확보하며 마을을 넓히는 **노드 선택형 확장**이다(GDD §6.3).

- 보유 영토 기준 **인접 영토만** 선택 가능(GDD §6.3 노란색/회색).
- 인접 영토 중 하나 선택 → 확보 → **고유 효과 획득** → 다음 확장 가능 영역(프론티어) 생성.
- 영토 효과·보상은 **매 게임 랜덤**(GDD §7) → 리플레이성. 마나석·주민이 대표 보상(§4.2·§6.1).

---

## 2. 핵심 설계 원칙 (요구사항 → 아키텍처)

세 요구사항(비그리드 / 효과 TBD / 인접 형태 TBD)을 **세 가지 분리**로 흡수한다.

### 원칙 1 — 인접은 "모델의 엣지"다 (시각 형태와 분리)
"두 영토가 인접한가"는 **그래프 엣지**로만 정의한다. 시각적으로 **딱 붙어 있든, 섬처럼 떨어져 다리로 이어지든**
그건 **뷰의 렌더링 결정**일 뿐 — 엣지가 있으면 인접, 없으면 아니다. → **요구사항 1(비그리드)·3(인접 형태 TBD)을
모델 변경 없이 흡수.** 그리드 좌표·스냅·셀 점유 개념이 아예 없다(전투 공간과의 결정적 차이).

### 원칙 2 — 영토 정의·효과는 SO 주입 + 효과 심으로 확장
각 노드는 **어떤 영토인지/무슨 효과인지**를 `TerritoryDefinition`(ScriptableObject) **참조**로 갖는다.
효과는 **추상 심(seam)** 뒤에 둔다(§5). 지금 효과 종류가 미정이어도 심만 있으면 되고, 나중에 **새 효과 SO를
붙이는 것만으로** 그래프·전이 코드 변경 없이 확장된다. → **요구사항 2(효과 TBD, SO 주입) 흡수.**
(muchan의 `BuildingAsset` "SO 참조 + 호출부가 Data 채움" 계보와 동일 패턴.)

### 원칙 3 — 노드는 free-form 위치
노드 위치는 그리드가 아니라 임의 좌표(2D 캔버스 or 3D 월드 — §8 TBD). 모델은 위치를 **데이터로만** 들고,
배치·연출은 뷰가 한다. → **요구사항 1 흡수.**

---

## 3. 데이터 모델 (제안)

**로직(순수 C# 모델) ↔ 뷰(렌더/입력)** 분리는 경영 자원 시스템(Resources.md)과 동일 원칙.

```
[정적 데이터]              [런타임 모델(순수 C#)]                 [뷰]
 TerritoryDefinition(SO) ──▶ TerritoryNode  ──┐
   DisplayName/Desc          Id/Position       │
   Effect(심)                State/Definition   ├─▶ TerritoryGraph ──▶ TerritoryGraphView
   (아이콘/색 등)             Neighbors[]        │     Owned/Frontier      (노드·연결 렌더 +
                                                ┘     전이·질의 API        ISelectable/IHoverable 연결)
```

| 요소 | 종류 | 역할(제안) |
|---|---|---|
| `TerritoryDefinition` | ScriptableObject | 영토 **정체성·효과** 데이터. 표시명·설명·효과 심(§5)·시각 힌트. **주입 대상**(요구사항 2) |
| `TerritoryNode` | 런타임 데이터 | 한 영토 인스턴스: `Id`, `Position`, `State`, `Definition`(SO ref), `Neighbors`(엣지) |
| `TerritoryGraph` | 순수 C# 모델 | 노드/엣지 소유. `Owned`·`Frontier` 집합 관리, 선택 전이·질의 API. UI 무관 |
| `TerritoryGraphView` | MonoBehaviour(뷰) | 노드·연결 렌더, 각 노드에 `ISelectable`/`IHoverable` 부착, 모델 갱신 구독 |

**노드 상태(enum 제안)**:
- `Owned` — 보유(GDD 노란색). 본진은 최초 `Owned`.
- `Selectable` — 프론티어(GDD 회색). 보유 영토에 인접 → 선택 가능.
- `Locked` — 그 외. 선택 불가(프론티어 확장으로만 해금).

---

## 4. 생성 · 공개 · 상태 전이

### 4.1 생성 — 런 시작 시 전체를 미리 만든다
- **런당 유한 그래프. 노드 최대 30**(경영 공간 ≤ 30, GDD 맥스 스테이지 30). 그 안에서 실제 활성/해금
  개수와 "특정 조건마다 열림" 규칙은 TBD.
- 런 시작 시 **전체 그래프를 랜덤 시드로 한 번에 생성**(노드 위치·엣지·영토 배정)하고 숨겨둔다.
  매 런 다른 위치·다른 영토 → 리플레이성(GDD §7).
- **알고리즘: Delaunay 삼각분할 → 프루닝**
  1. 최소 간격을 두고 랜덤 위치 산포(겹침 방지), 본진 기준 바깥으로 편향.
  2. Delaunay 삼각분할로 **평면 그래프**(엣지 교차 없음 → 가독성) 구성.
  3. 본진 기준 스패닝 트리는 보존(전 노드 도달성). 트리 성장은 **짧은 엣지 가중 룰렛**
     (가중치 = 1/길이^`TreeShortEdgeBias`)의 랜덤화 Prim — hop 깊이가 실제 거리와 대체로 일치해
     본진에서 바깥으로 **방사형**으로 자란다(균등 랜덤은 본진→림 장거리 엣지가 트리에 들어가
     확장 순서가 들쭉날쭉해지는 문제가 있었음).
  4. 나머지 삼각분할 엣지는 **길이 상한**(트리 평균 길이 × `ExtraEdgeMaxLengthRatio`) 안에서
     **일부만 랜덤 유지**(`ExtraEdgeKeepRatio`) → 사이클·교차 연결 형성(이미지 3의 되돌아 잇는 연결).
     장거리 되돌이 엣지는 다른 노드를 스치듯 지나 영토 에셋이 엉키므로 상한으로 차단.

### 4.2 공개 — 미리 만든 그래프를 점진적으로 드러낸다
- 플레이어에겐 **프론티어만** 보인다: `Owned` + `Selectable` + 이미 드러난 노드·엣지. `Locked`는 **숨김**.
- 확장으로 `Locked`가 `Selectable`이 되면 그 노드와 연결이 드러난다(플레이어에겐 "새로 생성"되는 느낌).
- **핵심**: 교차 연결은 미리 깔려 있던 엣지의 양끝이 드러나며 자연히 보인다. 즉 "안 가본 기존 노드와
  이어지는" 동작은 풀어야 할 문제가 아니라 **미리 생성의 공짜 결과**다(즉석 생성이었다면 확장할 때마다
  근접 탐색·겹침·엣지 중복 판단이 필요 — 그 복잡도를 통째로 제거).

### 4.3 상태 전이 (GDD §6.3)
1. **초기화**: 본진 노드 = `Owned`. 그 인접 노드 = `Selectable`(공개). 나머지 = `Locked`(숨김).
2. **선택**: `Selectable` 노드를 확보 → `Owned`로 전이 → **효과 1회 적용**(§5) →
   그 노드의 이웃 중 `Locked`를 `Selectable`로 승격·공개(프론티어 갱신).
3. **불가**: `Owned`/`Locked`는 선택 불가(입력 무시). `Selectable`만 확정 대상.
4. 전이 시 모델이 변경 이벤트를 발생 → 뷰가 색/가시성 갱신(경영 자원 시스템의 갱신 이벤트 패턴 재사용).

---

## 5. SO 주입 & 효과 확장 심 (요구사항 2 핵심)

효과 종류가 **아직 미정**이므로, 지금은 **심만 정의**하고 구체 효과는 후속에 SO로 추가한다.

- `TerritoryDefinition`(SO)이 하나 이상의 **효과**를 참조한다.
- 효과 심(택1, §8에서 확정):
  - **(권장) 추상 효과 SO** — 효과를 ScriptableObject 서브클래스로 두고, 적용 시 컨텍스트를 받아 실행한다.
    새 효과 = 효과 SO 하나 추가. 데이터+행동을 SO로 주입 → 요구사항 2에 가장 부합.
  - 대안: 순수 인터페이스 + 코드 구현 (SO 주입성이 약함).
- **적용 컨텍스트**가 효과가 건드릴 시스템을 노출한다: 자원 지갑(마나·자원 지급),
  주민 획득 심(주민 시스템 부재 → placeholder), 대상 노드/그래프 참조 등.
- **신규 효과가 생겨도 `TerritoryGraph`/전이/뷰 코드는 불변** — 효과 SO만 추가·주입.

> **표시 문자열·수치 데이터 출처는 미결(WL-013·팀 계약 #2 연동)**: 표시명/설명을 CSV→SO 파이프라인
> (BuildingTable식)으로 둘지, authored SO로 둘지는 §8 TBD. 효과 **행동**은 코드/SO이므로 CSV POCO 패턴과 별개.

---

## 6. 통합 계약 (기존 심 재사용)

새 매커니즘을 만들지 않고 이미 있는 심에 참여한다.

| 접점 | 방식 |
|---|---|
| **선택 입력** | 노드가 `ISelectable` 구현 → `MouseManager`가 Idle 클릭으로 선택 통지(팀 계약 #1 입력 단일 창구). 확정 판정은 모델(`Selectable`인가)이 담당 |
| **호버** | **확정(이슈 #67, 최초 설계에서 변경)** — 노드가 `IHoverable`을 구현하지만 `GetTooltipContent()`는
`null`을 반환해 `TooltipUI` 툴팁은 띄우지 않는다(건물 호버 툴팁 경로와 독립). 대신 `OnHoverEnter`/
`OnHoverExit`(`MouseManager.md` §8 호버 하이라이트)로 Selectable 노드만 색 변경 — "표시명+효과 설명
pull 공급"으로 예정했던 원래 설계는 효과 카탈로그(§5)가 아직 없어 보류, 필요해지면 후속 |
| **자원 보상** | 효과가 `ResourceWallet.Add`로 마나석 지급(GDD §4.2 / 팀 계약 #3 — 마나석은 영토 확장·전투 보상에서만. **정당한 마나 원천**). 주민 획득(§6.1)은 주민 시스템 부재로 placeholder 심 |
| **낮/밤** | 확장은 **낮 행동**(GDD §5.1). `TerritoryController`가 `DayNightManager.OnDayStart`를 구독해 `HasExpandedToday`를 매 낮 시작마다 초기화하고, `TryClaim`에서 하루 1회로 게이팅한다(이슈 #67). 확장을 마쳐야(`HasExpandedToday == true`) `ManagementController`의 주민 배치가 열린다(§6.1 연동, 아래 참고). 밤 잠금·자원 비용 게이팅은 여전히 TBD(§8) |
| **공간 분리** | 경영 공간 전용. 전투 그리드(BattleMapBuilder)·좌표계와 **무관**(팀 계약 #4 — 한쪽 확장이 다른 쪽 상태에 의존 금지) |

---

## 7. 로직/뷰 분리 (실제 아트 교체 대비)

- `TerritoryGraph`(모델)는 상태·전이·질의만 안다. 렌더링·입력 위젯을 모른다.
- `TerritoryGraphView`가 노드/연결을 그리고 `ISelectable`/`IHoverable`을 연결, 모델 `OnChanged` 구독해 갱신.
- **인접 형태(밀집 vs 섬+다리)는 전적으로 뷰의 렌더링 결정** → 모델·전이·효과는 그대로(요구사항 3).
  다리 = 엣지의 한 시각 표현일 뿐.

### 7.1 현재 뷰 구현 (#127, PR#128 — muchan)

이 절의 "아트 교체 시 뷰 참조만 재연결" 원칙이 실제로 실행된 첫 사례. 모델·전이 코드는 무변경.

- **상태→비주얼 스왑**: 신형 프리팹 `TerritoryNodeV2` 루트의 `TerritoryNodeStateVisual`이 담당 —
  Selectable=소용돌이(`VortexVisual`, 에셋 부재로 절차 생성: 스파이럴 텍스처+회전 쿼드),
  Owned=산 에셋(@NorthLand Mountain_01~06, `nodeId % 6` 결정적 선택), 본진=스폰 안 함(씬의 섬 지형 사용).
- **확보 연출**: `OnNodeClaimed` 구독으로 확보 직후 1회만 — 소용돌이 스핀업+축소 소멸 → 산이
  ease-out-back으로 솟아오름. UniTask + `destroyCancellationToken`(Tower/AuraTower 패턴).
- **폴백**: `TerritoryNodeView`는 `TerritoryNodeStateVisual`이 없으면(구형 프리팹) 기존 색상 경로를 그대로 사용.
- **색 시맨틱 재배정 주의**: GDD §6.3의 "회색=선택 가능"이 소용돌이 도입으로 바뀜 —
  선택 가능=파란 소용돌이, **회색=오늘 확장 소진**(호버 하이라이트=밝은 틴트+회전 가속).
  아트 확정 시 GDD 색 언어 갱신 필요(PR#128 리뷰 🟡).
- **튜닝 세트 결합**: 그래프 간격(AreaRadius 450·MinNodeSpacing 140)과 비주얼 스케일(산 0.5·소용돌이
  지름 60·클릭 콜라이더 45)은 세트 — 간격류 조정 시 비례해 함께 조정할 것(WL-059).
- **잔여**: 본진 전용 연출. 다리(엣지)는 #93에서 배 왕복 연출로 대체됨(§7.2).

### 7.2 엣지 배 연출 (#93 — muchan)

§7.1과 같은 "엣지 = 한 시각 표현"(§7 원칙) 두 번째 적용. **모델·전이 코드 무변경, 뷰만 교체**.

- **선 → 배 교체**: `TerritoryGraphView`가 엣지마다 만들던 `LineRenderer`를, @NorthLand `SweetBoat_01~05`
  중 **랜덤 1척**이 두 노드 사이를 왕복하는 연출로 대체. 새 `TerritoryEdgeShip`(뷰 내부 컴포넌트)이
  이동/조향 담당 — 엣지 길이와 무관하게 **속도 일정 왕복**, 진행 방향 바라보기(끝점 반전 시 `RotateTowards`
  로 완만하게), 배 FBX의 forward 축이 불명이라 `_shipYawOffset`으로 뱃머리 보정. 선은 `_drawEdgeLines`로
  분리해 **기본 꺼짐**(디버그용 보존).
- **표시 규칙 강화(설계 변경)**: §4.2의 엣지 공개(양끝이 드러나면 보임)와 달리, 배는 **양끝이 모두 `Owned`
  일 때만** 흐른다 — 이를 위해 `TerritoryGraph.IsOwned(id)` 신설, `Refresh`가 `IsOwned(A) && IsOwned(B)`로
  게이팅. 확보 가능(Selectable) 프론티어로 뻗는 엣지엔 아직 배가 다니지 않는다("확보된 영토 사이 물류" 톤).
- **선택 레이캐스트 간섭 차단**: 배 프리팹의 `MeshCollider`를 인스턴스 시 제거 — 배가 노드 루트 콜라이더
  클릭을 가로채지 않게(§3 접점 매트릭스, #127 "산 자식 콜라이더 스폰 시 비활성"과 동일 취지).
- **튜닝**: `_shipSpeed`·`_shipYawOffset`·`_shipHeightOffset`·`_shipEndpointInset`·`_shipTurnSpeed`를
  `TerritoryGraphView` 인스펙터에 노출(플레이 중 눈으로 맞춤 — 특히 뱃머리 방향·속도).

---

## 8. 미결 / TODO (구현 전 확정 필요)

- [ ] **영토 종류·효과 카탈로그** (요구사항 2): 효과 심 위에 올릴 구체 효과 미정. 심만 먼저, 효과 SO는 후속.
- [ ] **효과 연결 경로 고정** (WL-030, PR#75 리뷰): 현재 `OnNodeClaimed` 훅만 존재(더미 효과도 미연결).
      연결 시 `OnNodeClaimed → 효과 SO.Apply → ResourceWallet.Add`로만 지급(계약 #3), 수치는
      DataTable/CSV(계약 #2) — 인스펙터/코드 하드코딩 지급 금지.
- [x] **입력 연결 선결 조건(레이어)** (WL-005): **해소(이슈 #67)** — `TerritoryNode.prefab`이 Layer 6
      (`Selectable`)이고 `GameScene`의 `MouseManager._selectableMask`도 이 비트를 포함해 클릭·호버
      모두 정상 동작함을 실제 씬에서 확인. 클릭 1회=즉시 확보(비가역)가 `ISelectable`의 "조회" 시맨틱을
      오버로드하는 점은 여전히 유효 — 비용·게이팅 도입 시 미리보기/확정 단계 분리 검토는 계속 TBD.
- [ ] **전투 영토 확장(#1)과 모델 공유/분리 판정** (WL-029, PR#75 리뷰): 이슈 #18이 요구한 데이터 구조 공유
      검토 미결. 합의 후 `Territory*` 타입군(현재 전역 12종)을 네임스페이스로 격리해 일반명 충돌 예방.
- [ ] **인접 형태** (요구사항 3): 밀집 vs 섬+다리 — **뷰 결정, 모델 영향 없음**(Delaunay 평면이라 엣지 교차는
      없음; 노드 간격·다리 표현만 뷰가 정함).
- [x] **그래프 생성 방식**: **확정** — §4.1 런 시작 시 미리 전체 생성 + Delaunay 삼각분할 프루닝 + 프론티어 공개.
- [ ] **노드 해금 조건·개수**: 최대 30 내에서 실제 활성/해금 개수와 "특정 조건마다 열림" 규칙 미정(TBD).
- [x] **확장 규칙(하루 1회)**: **확정**(이슈 #67) — 낮 시작(`DayNightManager.OnDayStart`)마다
      `TerritoryController.HasExpandedToday`를 초기화, `TryClaim`에서 하루 1회만 허용. 그 날 확장을
      마쳐야 `ManagementController.CanAssignVillagers`가 열려 주민 배치가 가능해진다(GDD §6.1 "영토
      확장을 통해 주민 획득"과 정합). 자원(마나 등) 비용 게이팅은 여전히 TBD.
- [ ] **보상 구체 수치**: 마나량·주민 획득 수 출처(CSV 파이프라인? 계약 #2). 주민 획득은 주민 시스템 의존.
- [ ] **좌표계**: 2D 캔버스(uGUI) vs 3D 월드 노드. 비그리드지만 공간 선택·호버가 필요 → 입력/레이캐스트 방식 결정.
- [ ] **표시 문자열 소유권**: TerritoryDefinition 표시명/설명을 CSV→SO vs authored SO (WL-013 연동).
- [ ] **세이브/로드**: 런 내 그래프 상태(보유/프론티어) 영속화.

---

## 9. 범위 밖 / 의존

- ❌ **주민 시스템**(GDD §6.1): 부재 → 주민 보상은 placeholder 심(자원 시스템의 주민 수 placeholder와 동일 상황).
- ❌ **구체 효과 구현**: §5 심 위 실제 효과들은 후속.
- ~~❌ **실제 아트/연출**: 첫 구현은 기능 배치(노드·연결 최소 시각), 아트 교체 시 뷰 참조만 재연결.~~
  → **노드 비주얼(#127, §7.1)·엣지 배 연출(#93, §7.2) 적용됨**. 잔여: 본진 전용 연출.
- **의존**: `MouseManager`(ISelectable/IHoverable, 씬 배치·`_camera`), `TooltipUI`(#38), `ResourceWallet`
  (`ManagementController` 소유, WL-017), `DayNightManager`(낮/밤 게이팅, nullable).

---

## 10. 문서 반영 예정 (구현 PR에서)

- `SystemMap.md` §1(소유자에 TerritoryGraph 추가) · §2(공개 API: `TerritoryGraph`·`TerritoryDefinition`·
  `TerritoryEffect` 심) · §3(접점: MouseManager/TooltipUI/ResourceWallet/DayNightManager).
- `WatchList.md`: 좌표계·표시 문자열 출처(WL-013) 등 신규/연동 이슈.

---

*이 문서는 설계 합의용 초안이다. §8 TBD가 확정되는 대로 갱신하고, 구현 착수 시 "설계" 표기를 해제한다.*
