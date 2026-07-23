# TerritoryGraph — 경영 영토 확장 (그래프 노드 선택) [설계]

> **담당**: n0wst4ndup
> **이슈**: #TBD
> **경로(코드)**: `Assets/Scripts/ManagementSpace/Territory`
> **상태**: ✅ **씬 통합 완료(이슈 #67)** — `TerritoryController`/`TerritoryGraphView`가 실제
> `Assets/Scenes/GameScene.unity`에 배치돼 그래프 생성·확보(`ISelectable`)·호버 하이라이트
> (`IHoverable`)까지 플레이 가능. ✅ **노드 비주얼 에셋 적용(#127, PR#128, muchan)** — 임시 색상
> 구체를 상태별 비주얼(소용돌이/산)로 교체, §7.1 참고. ✅ **엣지 배 연출 적용(#93, muchan)** —
> 엣지 선(LineRenderer)을 배(SweetBoat) 왕복 연출로 교체, §7.2 참고. ✅ **영토 = 미개척 영지 자원 재설계(#166)** —
> 구 효과 SO 계층 폐기, 각 영지가 매일 자동 수급하는 자원 SO(`TerritoryDefinition`)로 교체, §0·§5 참고.
> 밸런싱 수치·본진 연출 등 잔여 TBD는 §8에서 계속 추적한다.
> 확정되지 않은 항목은 본문에서 **TBD / TODO**로 명시한다(docs-are-dev-reference 규약).
> **GDD 근거**: §4.1(두 공간) · §5.1(낮 경영—영토 확장) · §6.3(경영 영토 확장) ·
> §4.2(마나석=영토 확장 보상) · §6.1(주민 획득) · §3(Slay the Spire 노드 선택 응용) · §7(랜덤 리플레이)

이 문서는 경영 공간의 **영토 확장 시스템이 무엇을 하고, 어떤 경계로 다른 시스템과 만나는지**의 설계 기준선이다.
전투 공간의 그리드 맵(BattleMapBuilder)과 달리 **정형화되지 않은 그래프**로 확장한다(GDD §4.1 독립 이중 확장).

---

## ✅ 0. 영토 = 미개척 영지 자원 재설계 (완료 — #166, GDD v0.3)

**영토의 의미가 바뀌었다.** 종전에는 영토를 확보하면 자원/주민/생산배율 등 **고유 효과**를 즉시/패시브로 얻었다(구 효과 SO 계층). 이제 **각 영토는 "미개척 영지"이고, 확보하면 그 영지 고유의 새 자원 종류(금/루비/사파이어/다이아 등)를 매일 자동 수급한다**(GDD §3.2·§5.3). 수급 라인은 **주민 배치 없이 매 정산마다 일정량이 자동 수급된다**(Resources.md §5.5).

- **재편 완료(#166)**: 구 효과 SO 계층(`TerritoryEffect`/`GrantResourceEffect`/`GainResidentEffect`/`ProductionMultiplierEffect`/`TerritoryEffectContext`)을 **삭제**하고, `TerritoryDefinition`을 "자원 영지 정의"(자원 종류 + 섬 프리팹 + 일일 수급 [Min,Max])로 리셰이프했다(§5). 확보 즉시 지급 경로(`OnNodeClaimed → ApplyAll`)는 제거되고, **매일 정산 자동 수급**(`ManagementController.HandleNightToDay`)으로 교체됐다.
- **구조 코드는 그대로**: 그래프 생성(Delaunay+프루닝)·선택·점진 공개·뷰 연출(소용돌이/섬·엣지 배)·하루 1회 게이팅은 무변경. 확보 방식 자체는 바뀌지 않았다.
- 아래 §5·§6·§8은 이 재설계 기준으로 갱신됨.

---

## 1. 목적 · GDD 의도

본진에서 시작해 인접 영토를 하나씩 확보하며 마을을 넓히는 **노드 선택형 확장**이다(GDD §6.3).

- 보유 영토 기준 **인접 영토만** 선택 가능(GDD §6.3 노란색/회색).
- 인접 영토 중 하나 선택 → 확보 → **새 미개척 영지 해금**(새 방향, §0) → 다음 확장 가능 영역(프론티어) 생성.
  - _(종전: 확보 → 고유 효과 획득. 효과 SO 방식은 §5, 재편 예정 §0.)_
- 확보로 얻는 것은 **그 영지 고유의 새 자원 종류 + 생산 라인**(GDD §3.2·§5.3). 어떤 영지가 어디서 열리는지는 **매 게임 랜덤**(GDD §7) → 리플레이성.

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

## 5. 자원 영지 SO 주입 (#166)

**각 노드는 "미개척 영지 정의"(`TerritoryDefinition`) SO 하나를 주입받는다.** 구현 타입:

- `TerritoryDefinition`(SO, `Assets/Scripts/ManagementSpace/Territory/TerritoryDefinition.cs`):
  - `ResourceKind Kind` — 이 영지가 매일 수급하는 자원(금/루비/사파이어/다이아 등).
  - `GameObject IslandPrefab` — 확보 시 이 노드에 세워지는 섬 프리팹. **SO = 고정 프리팹**(종전 `TerritoryNodeStateVisual._mountainPrefabs`의 노드 Id 기반 선택에서 SO 소유로 이관).
  - `int MinDaily`/`MaxDaily` + `int RollDailyYield(System.Random)` — 매일 수급량 범위. **주입 시점에 [Min,Max]에서 1회 롤**해 노드에 확정.
  - 표시명/설명 스트링 테이블 키(`NorthLand_Territories`, `territories.{id}.name/.desc`).
- `TerritoryNode.Definition`(SO ref) + `TerritoryNode.DailyYield`(롤된 일일량) — 노드가 보관하는 주입 결과.
- **주입·롤**: `TerritoryController._definitionPool`(authored SO 리스트)을 그래프 생성 시 `_seed`로 셔플해 본진 제외 노드에 배정하고, 각 노드의 `DailyYield`를 같은 rng로 롤(`AssignDefinitions`). **자원 종류(≈4종) < 노드(최대 30)라 같은 자원이 여러 노드에 정상 재등장**(GDD §3.2 "영지 수↑→총 수급↑"). 같은 seed면 지형+영지 배치+일일량이 재현된다(WL-008).
- **수급(즉시 아님)**: 확보 즉시 지급은 없다. `ManagementController.HandleNightToDay`가 매 정산마다 `Graph.Nodes` 중 Owned+Definition 노드를 순회해 `ResourceWallet.Add(Definition.Kind, DailyYield)` — **주민 배치와 무관한 매일 자동 수급**(Resources.md §5.5 A).
- **새 자원 종류가 생겨도 `TerritoryGraph`/전이/뷰/주입 코드는 불변** — SO만 추가하고 풀에 넣으면 된다.

> **⚠ 구 효과 SO 계층 삭제(#166)**: `TerritoryEffect`/`GrantResourceEffect`/`GainResidentEffect`/`ProductionMultiplierEffect`/`TerritoryEffectContext`와 `Definition.ApplyAll` 즉시 적용 경로는 **폐기·삭제**됐다. `ProductionModifiers`(생산 배율 레지스트리)는 코드에 잔존하나 등록 생산자가 없어 항상 ×1이다(기본 라인 정산·예상치 호출부 무변경 목적).

> **수치 데이터 출처: 영지 SO 단독 관리(CSV 아님)** — 팀 회의 결정. 각 SO 인스펙터에 자원 종류·섬 프리팹·일일 수급 범위를 직접 authoring한다. 계약 #2(수치=CSV)는 **건물/타워/적 밸런싱 테이블에 적용**되며 영지 데이터는 그 예외. 표시명/설명은 스트링 테이블 키(로컬라이제이션 #102 계보).

---

## 6. 통합 계약 (기존 심 재사용)

새 매커니즘을 만들지 않고 이미 있는 심에 참여한다.

| 접점 | 방식 |
|---|---|
| **선택 입력** | 노드가 `ISelectable` 구현 → `MouseManager`가 Idle 클릭으로 선택 통지(팀 계약 #1 입력 단일 창구). 확정 판정은 모델(`Selectable`인가)이 담당 |
| **호버** | 노드가 `IHoverable` 구현. **툴팁 공급**: `GetTooltipContent()`가 노드에 주입된 `TerritoryDefinition`의
이름·설명을 `LocalizationHelper.Get(k_TerritoriesTable, DisplayNameKey/DescriptionKey)`로 동기 pull해
`TooltipContent`로 반환한다(`BuildingTooltipSource` 계보) — 정의가 없는 노드(본진·미할당)는 `null` 반환으로
툴팁 없음. 키는 정의의 `_id`에서 `territories.{id}.name/.desc`로 파생(스트링 테이블 `NorthLand_Territories`).
색 하이라이트는 `OnHoverEnter`/`OnHoverExit`(`MouseManager.md` §8)가 별도로 Selectable 노드만 담당 |
| **자원 수급** | 확보(Owned) 영지가 **매일 정산마다** 자기 자원(`Definition.Kind`)을 `DailyYield`만큼 `ResourceWallet.Add`로 지급(#166) — 주민 배치 무관한 자동 수급(GDD §3.2·계약 #3). **확보 즉시 지급·주민 획득·생산 배율 효과는 모두 제거됨**(구 효과 SO 계층 폐기, §5). 정산 주체는 `ManagementController.HandleNightToDay`(`OnNightToDay`) |
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

- [x] **🔀 영토 = 미개척 영지 자원 재설계**(§0, GDD v0.3, #166): **완료** — 확보 효과를 "고유 효과 지급"에서
      "매일 자동 수급하는 자원 영지"로 교체. 구 효과 SO 계층(§5)·`ApplyAll` 즉시 적용 삭제. 연동 구현: 정산부
      `HandleNightToDay`에 일일 수급 `Add`, 패널 고정 8행(기본3+마나+특수4, Resources.md §5.5).
- [x] **자원 영지 카탈로그** (#166): **구현됨**(§5) — `TerritoryDefinition`(자원 종류+섬 프리팹+일일 [Min,Max]).
      초기 4종 authored: `Territory_gold/ruby/sapphire/diamond`(placeholder 섬 = @NorthLand Mountain_01~04). 밸런싱 수치는 후속.
- [x] **일일 수급 정산 배선** (구 WL-030): **완료** — `ManagementController.HandleNightToDay`가 매 정산 시 `Graph.Nodes`의
      Owned+Definition 노드를 순회해 `ResourceWallet.Add(Definition.Kind, DailyYield)`. 확보 즉시 지급·`OnNodeClaimed` 효과 경로는 제거.
- [x] **구 패시브 생산 modifier 효과 제거** (#166): `ProductionMultiplierEffect` 삭제. `ProductionModifiers` 레지스트리는
      코드에 잔존하나 생산자가 없어 항상 ×1(기본 라인 정산·예상치 호출부 무변경 목적) — 필요 시 후속 정리.
- [x] **영지 분배 정책** (#166): `AssignDefinitions`는 자원 종류(≈4) < 노드(≤30)라 풀 소진 후 재셔플 반복 —
      **같은 자원이 여러 노드에 재등장하는 것이 정상**(영지 수↑→총 수급↑). 가중치·인접 회피 등은 필요 시 후속.
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
- [x] **보상 수치 출처**: **확정 — 효과 SO에 직접 authoring**(CSV 아님, §5 결정). 주민 획득은 여전히 주민 시스템 의존(심).
- [ ] **좌표계**: 2D 캔버스(uGUI) vs 3D 월드 노드. 비그리드지만 공간 선택·호버가 필요 → 입력/레이캐스트 방식 결정.
- [~] **표시 문자열 소유권**: `TerritoryDefinition`에 표시명/설명 **스트링 테이블 키 필드 존재**(authored, #102 계보).
      효과 카탈로그가 채워지면 실제 키 배정. CSV→SO 파이프라인은 도입 안 함(§5 SO 단독 결정과 정합).
- [ ] **세이브/로드**: 런 내 그래프 상태(보유/프론티어) 영속화.

---

## 9. 범위 밖 / 의존

- ❌ **주민 시스템**(GDD §6.1): 부재. #166에서 주민 획득 효과(`GainResidentEffect`)는 제거됨 — 영지는 자원만 수급한다.
- ✅ **자원 영지 구현**(#166): §5 `TerritoryDefinition`(자원 종류+섬+일일 수급) + `HandleNightToDay` 매일 수급. 초기 4종 authored.
- ~~❌ **실제 아트/연출**: 첫 구현은 기능 배치(노드·연결 최소 시각), 아트 교체 시 뷰 참조만 재연결.~~
  → **노드 비주얼(#127, §7.1)·엣지 배 연출(#93, §7.2) 적용됨**. 잔여: 본진 전용 연출.
- **의존**: `MouseManager`(ISelectable/IHoverable, 씬 배치·`_camera`), `TooltipUI`(#38), `ResourceWallet`
  (`ManagementController` 소유, WL-017), `DayNightManager`(낮/밤 게이팅, nullable).

---

## 10. 문서 반영 예정 (구현 PR에서)

- `SystemMap.md` §1(소유자에 TerritoryGraph 추가) · §2(공개 API: `TerritoryGraph`·`TerritoryDefinition`
  (자원 영지 SO, #166)·`ManagementController.SupplyDaily`) · §3(접점: MouseManager/TooltipUI/ResourceWallet/DayNightManager).
- `WatchList.md`: 좌표계·표시 문자열 출처(WL-013) 등 신규/연동 이슈.

---

*이 문서는 설계 합의용 초안이다. §8 TBD가 확정되는 대로 갱신하고, 구현 착수 시 "설계" 표기를 해제한다.*
