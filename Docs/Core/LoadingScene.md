# 로딩 씬 — 부팅 성능 측정 기준선 · 선행 가능 항목 대장

> **상태**: **측정 완료 · 설계 확정 · Phase 1 구현 완료 / Phase 2 미착수.**
> §1~§4는 2026-08-25 실측 기록, §5가 설계와 진행 상황이다. Phase 1 내역·검증은 §5.4, 남은 미결은 §5.5.
> **소유**: n0wst4ndup
> **이슈**: #442 [Feature] 로딩창 구현
> **측정 환경**: Unity 6000.3.15f1 에디터 / URP 17.3.0 / Windows 11 / unity-cli 커넥터 0.3.22
> **측정 대상 커밋**: `42f39b7` 시점의 `Assets/Scenes/GameScene.unity`
> **관련 파일**:
> - `Assets/Scenes/LoadingScene.unity` — (**Phase 1 배선 완료**) Canvas(+`CanvasGroup`) + Panel + `marshie-run-v1_0` + `LoadingSystem`(`LoadingFlow`·`LoadingScreen`). Build Settings 등재됨. ⚠ 마스코트 표시 문제는 §5.5
> - `Assets/Scripts/GameManager/GameSceneManager.cs` — 씬 전환 정본. `SceneManager.LoadScene` **동기** 호출 6군데(GameScene행 5 + TitleScene행 1)
> - `Assets/Scripts/SeedData/RunBootstrapper.cs` — `Start()`에서 시드 확정 → 전투맵 초기화(자가 부팅)
> - `Assets/Scripts/CombatSpace/Map/CombatMapGenerator.cs` — 맵 데이터 생성(**순수 연산**, §4.1)
>
> **참조**: `Docs/Core/SceneWorkflow.md`(정본 씬 규칙 — §5.3 충돌), `Docs/Core/CombatMapGeneration.md`, `Docs/Review/SystemMap.md`, `Docs/Review/WatchList.md`(WL-006), `Docs/Tools/unity-cli-guide.md`
> **문서 계약**: 코드가 이 명세와 어긋나면 문서를 갱신한다(팀 계약 #7). 실측치는 재측정 없이 소급 수정하지 않고, 새 측정을 날짜와 함께 덧붙인다.

---

## 0. 요지 (한 문단)

GameScene 진입 시 **부팅 프레임 하나에 976.77ms가 몰린다**(2회 재현). 이 프로젝트는 콘텐츠 에셋을
Addressables/AssetBundle로 쓰지 않고 전부 씬·프리팹 직접 참조라, `LoadSceneAsync`가 씬 에셋 로드를
이미 담당한다 — 따라서 로딩 씬이 해야 할 일은 "에셋 프리로드"가 아니라 **(a) 씬 활성화 직후 한
프레임에 몰리는 비용을 커튼으로 덮는 것**과 **(b) 씬과 무관하게 미리 끝낼 수 있는 연산·에셋 로드를
앞당기는 것** 둘이다. (b)에서 가장 큰 둘은 **전투맵 데이터 생성(117ms, 최악 5배 이상)**과
**`Resources.LoadAll<TowerAsset>`(콜드 586ms)**이며, 둘 다 GameScene 없이 실행 가능하다.

---

## 1. 측정 환경과 한계 — **먼저 읽을 것**

이 문서의 수치는 **에디터 측정**이다. 빌드와 세 방향으로 어긋난다.

| 항목 | 에디터 측정 | 빌드에서 어떻게 되나 |
| --- | --- | --- |
| `Mono.JIT` | 곳곳에 나타남(렌더 루프 안에서만 합계 160ms+) | **PC = 그대로 재현**(§4.3), **Android = 0**(IL2CPP) |
| 셰이더 컴파일 | 거의 안 보임 — 에디터 셰이더 캐시가 데워져 있음 | **과소 측정.** 빌드 첫 실행에서 드러날 수 있음 → **TODO: 재측정** |
| `Resources.Load*` | AssetDatabase 경유 | `resources.assets` 패킹본이라 더 빠름. 비율은 유지 |

> ⚠ **절대값이 아니라 항목 간 비율로 읽을 것.** "어디를 고칠지"의 순위표로는 유효하지만 빌드 절대
> 성능 예측으로는 쓸 수 없다. 빌드 실측은 아직 없다(**TODO**).

측정에 쓴 명령은 §6에 그대로 적어 두었다.

---

## 2. 부팅 프레임 실측 (2026-08-25)

에디터에서 `GameScene`을 직접 열고 플레이 모드 진입. **TitleScene을 거치지 않으므로 세이브 슬롯
미선택 경고 1건이 뜨는데, 측정에는 영향이 없다.**

| 프레임 | PlayerLoop |
| ---: | ---: |
| **0** | **976.77ms** |
| 1 | ~166ms |
| 2 이후 | ~7ms |

### 2.1 프레임 0 분해

| 구간 | totalMs | selfMs | 정체 |
| --- | ---: | ---: | --- |
| `DoRenderLoop_Internal` | **444.75** | 39.15 | URP RenderGraph 최초 컴파일 + Mono JIT |
| └ `RenderSingleCameraInternal: MinMapCamera` | 250.22 | 2.30 | **미니맵 카메라가 첫 렌더를 떠안는다** |
| &nbsp;&nbsp;└ `Inl_RecordRenderGraph` | 82.48 | 0.70 | 내부 `Mono.JIT` x470 = 45.06 |
| &nbsp;&nbsp;└ `Inl_CompileRenderGraph` | 80.48 | 0.98 | 내부 `Mono.JIT` x361 = 47.55 |
| &nbsp;&nbsp;└ `Inl_ExecuteRenderGraph` | 45.31 | 0.45 | |
| `ScriptRunDelayedStartupFrame`(= `Start()` 전체) | **276.25** | 0.00 | |
| └ `RunBootstrapper.Start()` | 196.15 | **117.77** | **self = 순수 맵 생성 알고리즘** |
| &nbsp;&nbsp;└ `Instantiate` x710 | 38.15 | 0.94 | 타일 스폰 |
| &nbsp;&nbsp;└ `Mono.JIT` x380 | 35.26 | 35.09 | |
| └ `ResidentSpawner.Start()` | 90.31 | 1.63 | 주민 30명 |
| &nbsp;&nbsp;└ `BehaviorGraphAgent.Awake()` x30 | 50.45 | 2.83 | Unity Behavior 그래프 초기화 |
| &nbsp;&nbsp;└ `Animator.Rebind` x30 | 13.47 | 0.01 | |
| └ `ManagementPanelView.Start()` | 11.29 | 1.49 | |
| └ `NextWavePreviewView.Start()` | 11.06 | 0.79 | 7.36ms가 `ReadObjectFromSerializedFile` |
| └ `TowerSelectPanelView.Start()` | 10.54 | 1.26 | |
| └ `PlayerSaveService.Start()` | 9.95 | 0.80 | 8.34ms가 `Mono.JIT` |
| `ScriptRunBehaviourLateUpdate` | **114.99** | 0.00 | |
| └ `ScrollRect.LateUpdate()` x2 | 95.36 | 0.50 | Layout 46.31 + PreRender 45.02 |
| `ScriptRunDelayedTasks` | 37.32 | 0.01 | |
| `ScriptRunBehaviourUpdate` | 29.54 | 0.00 | |

### 2.2 ⚠ 이 977ms는 **과소평가**다

프레임 0에 `DebugTowerSection.Start()`가 **4.16ms**로 잡혔다. 이 메서드는
`Resources.LoadAll<TowerAsset>`을 부르는데, 측정 직전에 같은 세션의 `exec`로 TowerAsset을 이미
메모리에 올려 둔 상태였다. **콜드 상태에서는 여기에 수백 ms가 붙는다**(§3.2). 즉 실제 콜드 부팅
프레임은 977ms보다 크다.

---

## 3. 에셋 로드 실측 (편집 모드, `exec` + `Stopwatch`)

### 3.1 CSV 데이터 테이블 — 합 11.4ms, **비용 아님**

| 항목 | 1회차 | 2회차(CsvHelper 워밍 후) |
| --- | ---: | ---: |
| `ResourceTable.Load` | 2.99ms | 2.05ms |
| `BuildingTable.Load` | 2.89ms | 2.21ms |
| `TowerTable.Load` | 2.86ms | — |
| `EnemyTable.Load` | 2.64ms | — |

`DataTableManager`는 **static 생성자**에서 이 넷을 로드한다(`DataTableManager.cs:10`). 비용 자체는
무시할 수준이지만 **"누가 처음 만지느냐"가 초기화 타이밍을 정하므로 시점이 비결정적**이다. 로딩
구간에서 한 번 접촉해 시점을 고정할 가치는 있다(비용 절감이 아니라 결정성 확보가 목적).

### 3.2 `Resources.LoadAll<TowerAsset>` — 콜드 **586.84ms**

| 항목 | 시간 | 개수 |
| --- | ---: | ---: |
| `Resources.LoadAll<TowerAsset>` **콜드** | **586.84ms** | 20 |
| `Resources.LoadAll<TowerAsset>` 웜 | 0.68ms | 20 |
| `Resources.LoadAll<TowerRecipe>`(TowerAsset 이후) | 5.13ms | 13 |
| `Resources.LoadAll<ScriptableObject>` 전체(웜) | 229.35ms | 118 |
| `Assets/Resources` 전체 크기 | 740KB | SO 117개 |

**웜이 0.68ms라는 것이 핵심 근거다.** SO 파싱 자체는 공짜이고, 586ms는 전부 `TowerAsset`이 물고
있는 **의존 에셋** 로드 비용이다:

```
TowerAsset (20개)
 ├ GameObject TowerPrefab      ← 20개 프리팹 + 메시·머티리얼·VFX
 ├ GameObject GhostPrefab      ← 20개 프리팹
 └ Sprite Icon
```

### 3.3 참조 그래프 — **13종이 씬에 없다**

`GameScene.unity`가 직접 참조하는 SO를 GUID로 세었다.

| Resources 폴더 | 씬 참조 / 전체 | 판정 |
| --- | ---: | --- |
| `ScriptableObjects/Towers` | **7 / 20** | ⚠ 13종이 지연 로드 |
| `ScriptableObjects/Wave` | 17 / 18 | 문제 없음 — `MonsterWaveGroup.MonsterPrefab`이 씬 로드에 딸려 옴 |
| `ScriptableObjects/Buildings` | 5 / 10 | 문제 없음 — **`BuildingAsset`은 프리팹·스프라이트 참조가 없는 순수 데이터** |
| `ScriptableObjects/BuffTiles` | 1 / 9 | 문제 없음 — 체인 검증됨(아래) |
| `ScriptableObjects/Enemies` | 0 / 9 | 문제 없음 — `Sprite icon`만 참조. 몬스터 프리팹 경유로 도달 |
| `ScriptableObjects/Reward` | 0 / 7 | **6 / 7**만 체인으로 도달. 1종은 고아(아래) |

**검증된 참조 체인** — "씬 직접 참조 0"이 곧 "지연 로드"는 아니다. 두 홉 이상 건너 도달하는 것은
`LoadSceneAsync`가 함께 올린다.

```
GameScene
 └ CombatMapGenerationSettings.asset        (씬 직접 참조)
    ├ BuffTileSpawnPool.asset  → BT_Damage_1/2/3 · BT_Range_1/2/3 · BT_NormalGrass  (7종)
    └ TileBuffRuleSettings.asset

GameScene
 └ MonsterWave 3 / 11 / 13.asset            (씬 직접 참조)
    └ WaveRewardPool.asset → BombReward · BurnReward · CountReward · ExecuteReward · FieldReward  (5종)
```

> ⚠ **`BuffBurnReward.asset`은 어디서도 참조되지 않는다**(`Assets/Resources`·`Assets/Scenes`·
> `Assets/Prefabs` 전수 검색 결과 0건). 로딩 성능과는 무관하지만 **고아 에셋**이므로 담당자 확인이
> 필요하다 — 등록 누락인지 폐기 잔재인지 판단해야 한다. **미결**.

**씬이 참조하지 않는 TowerAsset 13종은 정확히 합성 결과 타워다**: `Sniper` · `boomerang` ·
`flame_archer` · `gatling` · `incendiary_cannon` · `missile` · `multi_inferno` · `rampup` ·
`single_inferno` · `soda_cannon` · `soda` · `triple_shoot` · `twin_missile`.

`LoadSceneAsync`는 이 13종을 올리지 않는다. 그런데 `TowerRecipe.Result`(`TowerRecipe.cs:12`)와
`MaterialEntry.Tower`(`TowerRecipe.cs:23`)가 `TowerAsset` **직접 참조**라, 아래 중 하나가 처음
실행되는 순간 13종의 프리팹·고스트·아이콘이 한꺼번에 올라온다.

| 트리거 | 시점 |
| --- | --- |
| `DebugTowerSection.cs:77` | **부팅 프레임**(GameScene에 존재) |
| `TowerMergeTargetIndex.cs:43` `Build()` | 합성 대상 인덱스 최초 구축 |
| `FusionTowerCodexUI.cs:57` `Awake` → `LoadData()` | `Assets/Prefabs/UI/TowerCodex/FusionTowerCodex.prefab` 활성화 시 |
| `TowerMergePanelView.cs:67` | 합성 패널 최초 구축 |
| `RunSaveManager.Towers.cs:121` | 이어하기 복원 |

---

## 4. 선행 가능 항목 대장

"코드 수정만으로 로딩 구간에 옮길 수 있는가"로 분류했다. **오브젝트 풀링은 이 대장의 범위 밖이다**
— 풀을 도입하면 자연히 로딩에서 해결되는 항목이므로 여기서 중복 계산하지 않는다.

### Tier 1 — 크고, 구조가 이미 허락함

#### 4.1 전투맵 데이터 생성 — 117ms (최악 5배 이상) ★ 최우선

`CombatMapGenerator.TryGenerate(int)`(`CombatMapGenerator.cs:108`)는 **순수 연산이며 씬 오브젝트를
하나도 건드리지 않는다.**

- 의존이 `CombatMapGenerationSettings`(SO) + 시드(`int`) **둘뿐**
- 내부 생성기 `WaypointGenerator` / `WaypointOrderer` / `RouteGenerator` / `RouteValidator` /
  `GrassGenerator` / `GrassEroder` / `WaterGenerator`가 전부 평범한 C# 클래스(필드에서 `new`)
- 출력이 `CombatMapData`라는 순수 데이터 객체
- 설정 SO가 `Assets/Resources/ScriptableObjects/CombatMapSetting/`에 있어 **어느 씬에서든 로드 가능**

→ **GameScene 없이 LoadingScene에서 통째로 실행 가능하다.** `CombatMapData`를 씬 간에 넘기면
GameScene은 타일 스폰만 하면 된다. `GameSceneManager`가 이미 `pendingMasterSeed`/`pendingContinueData`를
같은 방식으로 전달하고 있으므로 전달 경로는 신설이 아니다.

> ⚠ **분산이 더 중요한 이유**: `maxGenerationAttempts = 5` 재시도 + `validatedSeeds` 폴백 경로가
> 있다(`CombatMapGeneration.md` §1). 나쁜 시드를 뽑으면 117ms가 아니라 **5배 이상**이고 그 위에
> 폴백이 또 붙는다. **최악 프레임을 예측할 수 없다는 것 자체**가 이 연산을 로딩으로 옮겨야 하는
> 근거다.

필요한 변경: `TryGenerate`를 MonoBehaviour에서 분리하거나(순수 클래스로 추출), 로딩 씬에 생성기만
얹고 결과를 `GameSceneManager` 경유로 전달.

#### 4.2 `Resources.LoadAll<TowerAsset>` + `TowerRecipeCatalog.All` — 콜드 586ms

로딩 구간에서 1회 호출로 해소된다. 둘을 같이 태워야 한다 — 어느 쪽을 먼저 불러도 40개 프리팹이
딸려 오므로(§3.3) 한쪽만 데워도 다른 쪽이 싸지지만, 호출 자체를 로딩으로 옮기는 것이 목적이다.

#### 4.3 Mono JIT — 200ms+ … **PC 빌드에서만**

`ProjectSettings/ProjectSettings.asset:836`:

```yaml
scriptingBackend:
  Android: 1        # IL2CPP
```

**Android만 IL2CPP로 명시돼 있고 Standalone(PC) 항목이 없다 → 기본값 Mono.** 따라서 측정된 JIT
비용이 PC 빌드에 그대로 남는다.

JIT는 메서드를 **처음 실행할 때** 무는 비용이므로, 로딩 중에 같은 코드 경로를 한 번 밟으면 거기서
지불된다. 렌더 경로 JIT는 **로딩 씬이 URP로 그려지는 것만으로 상당 부분 소모된다 — 별도 워밍업
코드가 필요 없다.** 로딩 씬의 카메라 구성을 GameScene과 맞추면(특히 2번째 카메라 `MinMapCamera`,
SSAO) 더 많이 흡수된다.

> **TODO(검증)**: Build Settings에서 Standalone 스크립팅 백엔드를 눈으로 확인할 것. 위 판단은
> "명시 설정이 없으니 Unity 기본값"이라는 추론이다. IL2CPP로 바꾸면 이 항목은 통째로 사라지고,
> 대신 §1의 셰이더 컴파일 항목이 상대적으로 커진다.

### Tier 2 — 작지만 코드 한두 줄

| 항목 | 위치 | 지금 무는 시점 | 비고 |
| --- | --- | --- | --- |
| `Shader.Find` + `new Material` | `RangeCircle.cs:92`·`:103` | 타워 처음 집을 때 | 사거리 원 |
| | `BeamAction.cs:250` | 빔 타워 첫 발사 | |
| | `TowerDissolveEffect.cs:296` | 합성 연출 첫 재생 | |
| | `TowerPlacer.cs:606` | 배치 고스트 첫 표시 | |
| | `GrainSwarm.cs:150` | VFX 첫 재생 | |
| 절차적 텍스처 `CreateGrainTexture(64)` | `GrainSwarm.cs:146` | VFX 첫 재생 | `s_grain` static, 세션 1회 |
| `Resources.GetBuiltinResource<Mesh>` | `GrainSwarm.cs:145` | 〃 | `s_quad` |
| `NavMesh.CalculateTriangulation()` | `ResidentCarryVisual.cs:547` | **첫 드래그** | 1.78ms + 정점 13,514개 배열 |
| Localization 동기 블로킹 | `LocalizationHelper.cs:49` | 첫 문자열 조회 | `GetTableAsync(...).WaitForCompletion()` |
| 웨이브별 지연 캐시 | `MonsterSpawnWaveProvider` | 각 웨이브 최초 요청 | `CollectSpawnPrefabs` / `cachedEntries` / `cachedComposition` |

`Shader.Find`는 셰이더 조회에 더해 첫 배리언트 로드를 동반한다. 로딩에서 미리 불러 static에 잡아
두면 된다.

`NavMesh.CalculateTriangulation()`은 이미 씬당 1회로 캐시돼 있으나(코드 주석에 1.78ms·정점 13,514개
실측이 적혀 있다) **첫 드래그가 그 비용을 문다.** 같은 주석이 "이 저장소에 런타임 재베이크가
0건"이라는 근거를 대고 있으므로 NavMesh는 로딩 시점에 이미 확정이다 → 그때 재도 정합하다.

### Tier 2-a — TMP 동적 아틀라스

| 폰트 에셋 | `m_AtlasPopulationMode` | 의미 |
| --- | ---: | --- |
| `Pretendard-Regular SDF` | 0 (Static) | 한글·영문 프리베이크 — 문제 없음 |
| `PretendardJP SDF` | **1 (Dynamic)** | 런타임 래스터화 |

`TMP Settings.asset`의 **전역 폴백**이 `PretendardJP SDF`다. 즉 Pretendard 정적 아틀라스에 없는
글자가 나오는 순간 JP 폴백으로 떨어져 **런타임 글리프 래스터화 + 아틀라스 텍스처 업로드**가 일어난다.

로딩에서 `TMP_FontAsset.TryAddCharacters`로 미리 찍는 것이 정석이나, **찍을 문자 목록을 먼저 정해야
한다**(String Table을 훑어 문자 집합을 뽑는 방식). 이 항목은 JP 정적 서브셋 재베이크(Phase 2)와
범위가 겹치므로 그쪽과 함께 결정한다 — **미결**.

### Tier 3 — 선행 연산으로는 못 옮김 (커튼 뒤 프레임 분산만 가능)

| 항목 | ms | 왜 못 옮기나 |
| --- | ---: | --- |
| `ScrollRect` 최초 레이아웃 | 95 | GameScene UI라 씬이 로드돼야 함. 커튼 뒤 `Canvas.ForceUpdateCanvases()`로 털 수는 있음 |
| `ResidentSpawner` 주민 30명 | 90 | 인스턴스화 자체(`BehaviorGraphAgent.Awake` 50ms 포함) |
| 타일 `Instantiate` x710 | 38 | GameScene의 tileRoot·프리팹이 필요 |

**이 223ms는 "미리 계산"으로 없앨 수 없고, 커튼이 씬 활성화를 덮어야만 가려진다.** §5의 설계
선택이 갈리는 지점이다.

---

## 5. 설계 — **확정 / 구현 진행 중**

### 5.1 확정된 결정 (2026-08-25)

| 결정 | 채택안 | 근거 |
| --- | --- | --- |
| 커튼 범위 | **안 B — Additive 커튼 유지형** | 안 A는 §4 Tier 3의 223ms와 `Start()` 잔여분을 그대로 노출한다 |
| 씬 전이 | `TitleScene` → `LoadingScene`(Single) → `GameScene`(Additive) → `SetActiveScene` → `LoadingScene` 언로드 | TitleScene 위에 얹으면 타이틀 카메라·UI·메모리가 게임 내내 남는다 |
| 자가 부팅 억제 | **명시적 진입점 분리** — 초기화 본체를 public 메서드로 빼고 `Start()`는 "외부 구동이 아니면 자가 부팅"만 남긴다 | 의도가 코드에 드러나고 문서화가 쉽다. `enabled` 토글은 "왜 꺼져 있는지"가 대상 파일에 안 드러난다 |
| 맵 생성 이관 | **`CombatMapGenerator`의 생성 로직을 순수 C# 클래스로 추출** | 어디서든 호출 가능해지고 테스트도 된다. 설정 SO를 두 씬이 각자 참조하면 조용히 다른 맵이 나온다 |
| 진행 방식 | **단계적** — 소유자 기준으로 Phase 1/2 분할(§5.4) | |

> **에디터에서 GameScene 단독 실행이 계속 되어야 한다 — 타협 불가.** 팀 일상 워크플로우이고
> §6.2 재측정 절차도 그것을 전제한다. 그래서 자가 부팅은 **제거가 아니라 조건부**다.

### 5.2 Additive 충돌 3종 — 실측 확인됨

LoadingScene 위에 GameScene을 Additive로 열어 센 결과: **카메라 3 · AudioListener 2 · EventSystem 2.**

| 충돌 | 처리 | 비고 |
| --- | --- | --- |
| 카메라 3개 | **GameScene 카메라를 켠 채로 두고** 로딩 캔버스를 Screen Space - Overlay로 덮는다 | ⚠ **끄면 안 된다** — 켜 두어야 §4.3의 RenderGraph·렌더 JIT 워밍이 커튼 뒤에서 일어난다. 끄면 커튼을 걷은 직후 그 445ms를 다시 문다 |
| AudioListener 2개 | GameScene 로드 직전 LoadingScene 쪽을 끈다 | Unity가 경고를 뱉는다 |
| EventSystem 2개 | 〃 | 로딩 중에는 입력을 받지 않는다 |

### 5.3 로딩 단계와 진행률

| 진행률 | 단계 | 회수 대상 |
| ---: | --- | --- |
| 0.00–0.10 | Localization `InitializationOperation` await · `DataTableManager` 접촉 | §4 Tier 2 |
| 0.10–0.30 | `Resources.LoadAll<TowerAsset>` + `TowerRecipeCatalog.All` | 586ms |
| 0.30–0.45 | **맵 데이터 생성**(`CombatMapData`) — *Phase 2* | 117ms(최악 5배) |
| 0.45–0.65 | `LoadSceneAsync(GameScene, Additive)` | |
| 0.65–0.95 | 준비 완료 대기 — *Phase 2에서 프레임 분산으로 전환* | Tier 3 223ms |
| 0.95–1.00 | `SetActiveScene` → 최소 표시 시간 대기 → 페이드아웃 → `UnloadSceneAsync` | |

### 5.4 Phase 분할 — 소유자 기준

**Additive 로드는 완료 시점에 GameScene의 `Awake`/`Start`가 이미 끝나 있고, 그때 LoadingScene은
아직 언로드 전이다. 따라서 Phase 1만으로도 §2의 부팅 스파이크가 커튼 뒤로 들어간다** —
`RunBootstrapper`를 건드리지 않아도 된다. 준비 완료 판정은 **이미 public인
`CombatMapInitializer.IsInitialized`** 를 폴링해 파일 수정 없이 얻는다.

| | **Phase 1** (n0wst4ndup 영역 + 공용) — **구현 완료** | **Phase 2** (sunjin1222 조율 필요) |
| --- | --- | --- |
| 대상 | 로딩 씬·플로우(신규) · `GameSceneManager`(미정·공동) · 코디네이터 3종 latch · Tier 2 워밍업 | `RunBootstrapper`(SeedData) · `RunSaveManager`(SaveData) · `CombatMapGenerator`/`TileSpawner` · `ResidentSpawner` 프레임 분산 |
| 586ms 프리로드 | ✅ | |
| 워밍업 · latch 수정 | ✅ | |
| §2 부팅 스파이크 | ✅ **커튼 뒤로 숨음** — 단 1프레임 프리즈는 남아 로딩 애니메이션이 멈춘다 | ✅ 프레임 분산으로 프리즈 자체 소멸 |
| 맵 생성 선행 | | ✅ |

#### Phase 1 구현 내역 (2026-08-25)

| 파일 | 변경 |
| --- | --- |
| `Assets/Scripts/GameManager/Loading/LoadingFlow.cs` | **신규** — 워밍업 → 게임 씬 Additive 적재 → 준비 완료 대기 → 활성 씬 전환 → 커튼 걷기 오케스트레이션 |
| `Assets/Scripts/GameManager/Loading/LoadingScreen.cs` | **신규** — 진행률 표시·커튼 페이드. 참조는 전부 선택 |
| `Assets/Scripts/GameManager/Loading/BootWarmup.cs` | **신규** — Localization·DataTable·TowerAsset·공유 시각 자원 워밍업 |
| `GameSceneManager.cs` | 게임 씬 진입 5경로를 `EnterGameScene()` 단일 통로로 모으고 로딩 씬 경유로 전환. `GameSceneName`·`IsGameplayScene` 공개. 로딩 씬이 Build Settings에 없으면 경고 후 옛 경로로 폴백 |
| `GrainSwarm.cs` | `PrewarmShared()` 공개 — 공유 쿼드 메시·절차 텍스처만 미리 생성. `EnsureAssets`가 이를 호출하도록 정리 |
| `OutlineInteractionDriver.cs` · `ResidentDragCoordinator.cs` · `ResidentSelectionCoordinator.cs` | 가드를 `!IsTitleScene` → `IsGameplayScene`으로 교정하고, 경고 latch를 `HandleSceneLoaded`에서 **씬 단위로 리셋**(§5.3-2) |
| `Assets/Scenes/LoadingScene.unity` | `LoadingSystem`(LoadingFlow+LoadingScreen) 추가, Canvas에 `CanvasGroup` 부착 |
| `ProjectSettings/EditorBuildSettings.asset` | `LoadingScene`을 TitleScene 바로 뒤에 등재(enabled) |

#### 로딩 문구 + 액체 채움 연출 (2026-08-25)

진행률 바를 **무작위 로딩 문구**로 대체했다. 문구에 분홍 액체가 차오르는 높이가 곧 진행률이다.

| 파일/에셋 | 변경 |
| --- | --- |
| `Assets/Scripts/GameManager/Loading/LoadingTipText.cs` | **신규** — 문구 무작위 선택·로컬라이즈·채움 연출 |
| `LoadingScreen.cs` | `tipText` 참조 추가, `Apply()`가 진행률을 그쪽으로 흘린다. **옛 `progressBar`/`progressText`(+ `Slider`·`TMPro` using)는 제거** — 대체됐고 씬에서도 비어 있었다 |
| `NorthLand_default` String Table | `loading.tip.*` **10키 × 3로케일** 추가(총 68 → 78키) |
| `LoadingScene`의 `Text (TMP)` | 자식으로 `FillMask`(RectMask2D) + `Fill`(TMP) 추가. `LoadingTipText` 부착 |

**문구 10종** — 마시멜로 마스코트와 CandyLand 테마에 맞췄고, 마지막 하나만 밤 페이즈를 가리킨다.

| 키 | ko-KR | en-US | ja-JP |
| --- | --- | --- | --- |
| `loading.tip.sweeten` | 달콤해지는 중 | Sweetening | 甘くしています |
| `loading.tip.crisp` | 바삭해지는 중 | Getting crispy | サクサク中 |
| `loading.tip.toast` | 마시멜로 굽는 중 | Toasting marshmallows | マシュマロを焼き中 |
| `loading.tip.sugar` | 설탕 뿌리는 중 | Sprinkling sugar | 砂糖をふりかけ中 |
| `loading.tip.syrup` | 시럽 붓는 중 | Pouring syrup | シロップをかけ中 |
| `loading.tip.dough` | 반죽 부풀리는 중 | Proofing the dough | 生地をふくらませ中 |
| `loading.tip.chocolate` | 초코 녹이는 중 | Melting chocolate | チョコを溶かし中 |
| `loading.tip.cream` | 크림 휘핑하는 중 | Whipping cream | クリームを泡立て中 |
| `loading.tip.oven` | 오븐 예열하는 중 | Preheating the oven | オーブンを予熱中 |
| `loading.tip.night` | 밤을 준비하는 중 | Bracing for night | 夜に備え中 |

**연출 구조 — 셰이더 없이 TMP 두 겹.**

```
Text (TMP)                     ← 레이아웃 행. 안 찬 글자(흰색) + LocalizeStringEvent
 └ FillMask   [RectMask2D]     ← 바닥 고정, 높이만 애니메이션 (0 → 행 높이)
     └ Fill (TMP)              ← 찬 글자. 정점 그라디언트 #FFD2C8(위) → #FF7896(아래)
```

- 두 TMP가 **글꼴·크기·정렬·사각형 높이가 같아야** 글자가 정확히 겹친다. 문구 길이에 따라
  `VerticalLayoutGroup`+`ContentSizeFitter`가 행 높이를 바꾸므로, `Fill`의 높이를 매 프레임
  `baseText`에서 받아 맞춘다.
- 문자열은 `LocalizeStringEvent.OnUpdateString`에 런타임 리스너를 하나 더 붙여 `Fill`에도 흘린다 —
  **로케일이 바뀌어도 두 겹이 같이 갱신된다.**
- TMP 정점 그라디언트는 **글자마다** 적용된다(줄 전체가 아니라). 액체 표현에서는 오히려 자연스럽다.
- `surfaceWavePixels`(기본 2.5px)로 표면을 살짝 넘실거리게 한다. 0이면 딱 잘린 수평선이라 액체로
  안 읽힌다. 다 찼거나 비었을 때는 넘실거림을 끈다.

**검증(2026-08-25)**: 플레이에서 `loading.tip.syrup`이 무작위로 뽑혔고 두 겹의 문자열이 일치했다.
채움을 0.5로 고정해 캡처한 결과 **위는 흰색, 아래는 분홍**으로 채움선에서 정확히 갈렸고, 표면 쪽이
밝은 `#FFD2C8`, 아래로 갈수록 `#FF7896`으로 깊어지는 그라디언트를 확인했다.

> ⚠ **JP 동적 아틀라스가 커진다.** 일본어 문구를 처음 렌더하면 `PretendardJP SDF.asset`에 글리프가
> 구워져 파일이 바뀐다(§4 Tier 2-a). 실제로 이 작업에서 164줄이 늘었다. 커밋 대상에 포함할지는
> 폰트 재베이크(Phase 2)와 함께 정한다.

**검증(2026-08-25)**: `refresh --compile` 후 **컴파일 에러 0**. LoadingScene에서 플레이 →
종료 시점에 `sceneCount=1` · 활성 씬 `GameScene` · `CombatMapInitializer.IsInitialized=true` ·
활성 AudioListener 1 · 활성 EventSystem 1. **콘솔 에러 0**이며 `MouseManager가 아직 없어…` 경고도
뜨지 않았다(§5.3-2 회귀가 실제로 막혔다는 확인). 남은 경고 1건은 `TowerButton.prefab` 배선 문제로
이 작업과 무관하다.

> **`ResidentSpawner` 프레임 분산은 Phase 1에서 뺐다 — 소유 문제가 아니라 순서 위험 때문이다.**
> `SpawnInitialCrowd`는 시작 시점에 `TargetCount`(= `crowdSize − AssignedTotal`)를 한 번 읽는다.
> 프레임에 걸쳐 나누면 그 사이에 `RunSaveManager`의 경영 복원이 끼어들어 `AssignedTotal`이 바뀌고,
> 군중이 목표를 넘겨 스폰된다. 복원 순서를 명시화하는 Phase 2와 같이 가야 안전하다.
> 어차피 Phase 1에서도 이 90ms는 커튼 뒤에 있으므로, 미루는 대가는 로딩 애니메이션의 매끄러움뿐이다.

#### 최소 표시 시간 · 커튼 z-order (2026-08-25, #442 2차 실측 반영)

| 지적 | 처리 |
| --- | --- |
| 로딩이 일찍 끝나면 마스코트가 뛰는 걸 못 보고 넘어간다 | `LoadingFlow.minimumDisplaySeconds` **1.2 → 3.4초** — `marshie-run-v1` 클립이 25프레임 @12fps = **2.083초/바퀴**라 약 1.6바퀴. 한 바퀴(2.1초)로는 짧아 눈으로 맞춘 값이다 |
| 게임 씬 UI가 커튼 **위로** 올라온다 | 로딩 `Canvas.sortingOrder` **0 → 1000**(`UILayer.LoadingCurtain`). 규약은 `Docs/Core/UIZOrder.md` §3 |
| 씬 전환을 인게임 낮/밤 전환처럼 | **보류** — 아래 참고 |

**왜 커튼이 덮이고 있었나.** `LoadingScene`은 `GameScene`을 Additive로 올린 뒤에도 살아 있는데
(§5.1), Screen Space - Overlay Canvas는 **씬과 무관하게 `sortingOrder`로만 전역 정렬**된다.
커튼이 `0`이면 `UICanvas`(100) · `RewardCanvas`(500) · `SettingCanvas`(700) · `ResultCanvas`(900)이
전부 커튼 위에 그려진다. 커튼은 항상 그 표의 최상단이어야 한다.

**타이밍 총합**: 최소 표시 3.4초 + 커튼 알파 페이드 0.35초 ≈ **3.75초**.

| 파일 | 변경 |
| --- | --- |
| `LoadingFlow.cs` | `minimumDisplaySeconds` 기본값 상향 + 근거 주석 |
| `UILayer.cs` | `LoadingCurtain = 1000` 추가 |
| `Assets/Scenes/LoadingScene.unity` | Canvas `sortingOrder = 1000`, `minimumDisplaySeconds = 3.4` |

> ⚠ 이 저장에서 `LoadingLayout`·마스코트·`Text (TMP)`의 `m_AnchorMin/Max`·`m_AnchoredPosition`·
> `m_SizeDelta`가 0으로 바뀐 diff가 같이 난다. 셋 다 `VerticalLayoutGroup`/`ContentSizeFitter`가
> **driven으로 잡는 값**이라 Unity가 저장하면서 비우는 것이고, 로드 시 레이아웃이 다시 계산한다
> (강제 리빌드로 `layout=(420.73, 265.89)` · `mascot=(420.73, 180)` 복원 확인). 되돌릴 필요 없다.

#### 커튼 전환 연출 — **보류**(2026-08-25)

"씬 전환이 인게임 낮/밤 전환(#101)처럼 보였으면 한다"는 요청으로 **셀 와이프를 시제작했다가
롤백했다.** 커튼은 기존 알파 페이드(0.35초) 그대로다. 재시도할 사람을 위해 확인된 사실만 남긴다.

- **`DayNightTransition`을 그대로 재사용할 수 없다.** 그것은 URP 풀스크린 렌더러 피처(`PC_Renderer`·
  `Mobile_Renderer`의 `Night Wipe`)라 **카메라 렌더 안에서** 돈다. 인게임에서 HUD를 안 덮는 것도
  같은 이유이고, 그래서 Overlay 캔버스인 로딩 커튼에도 닿지 않는다.
- **그 패스는 "리빌"을 못 한다.** `NightWipe.shader`는 뒤집힌 셀에 **색 그레이드를 얹을 뿐**이라
  아래 씬을 드러내지 못한다. 커튼을 셀 단위로 걷으려면 커튼 그래픽 쪽에 **셀별 알파**가 필요하다.
- **시제작 결과는 동작했다.** uGUI 머티리얼로 같은 무늬를 그리고(셀 임계값 식은 공유 `.hlsl`로 추출),
  커튼 `Panel`·마스코트 `Image`는 셀에 먹히고 TMP 문구만 `CanvasGroup`으로 페이드하는 구성이었다.
  편집 모드 프리뷰에서 우하단 → 좌상단으로 48px 셀이 지터와 함께 뒤집히고 선행 엣지가 얹히는 것까지
  확인했다. **기술적 문제가 아니라 "기존 자산으로 가자"는 판단으로 접었다.**
- 재개한다면 비용은 셰이더 1 + 머티리얼 1 + 컴포넌트 1이고, 인게임 튜닝값과의 **이중 관리**가
  남는 부담이다(무늬 식만 공유하고 파라미터는 각자 들고 있게 된다).

#### PR 리뷰 반영 (2026-08-26)

**1) 로딩 실패 시 커튼이 영원히 남는 경로 — 수정.** `RunAsync`의 `catch`가 로그만 남기고 커튼을
그대로 뒀다. `readyTimeoutSeconds`는 **전투맵 대기 구간에만** 걸려 그 앞뒤(워밍업 · `LoadSceneAsync` ·
`UnloadSceneAsync`)의 실패를 못 잡는다. 예를 들어 `Resources.LoadAll<TowerAsset>` 도중 에셋 참조가
깨져 예외가 나면 플레이어는 재시작 말고는 빠져나갈 방법이 없었다.

- `totalTimeoutSeconds`(기본 60초) 추가 — `LoadAsync` **전체**를 `UniTask.Timeout`으로 감싼다.
  단계마다 타임아웃을 심는 대신 바깥에서 한 번 재는 편이 빠뜨릴 자리가 없다.
  `readyTimeoutSeconds`(30초)보다 커야 흔한 실패에서 더 구체적인 쪽 로그가 먼저 뜬다.
- `LoadingFlow.Recover()` 추가 — 예외든 타임아웃이든 여기로 모인다. **동기이고 await가 없다**:
  복구 도중 또 실패하면 결국 커튼이 남기 때문에, 여기서는 연출보다 탈출이 먼저다.
  게임 씬이 올라와 있으면 `SetActiveScene` → `LoadingScreen.HideImmediately()` → 로딩 씬 언로드.
  게임 씬이 아예 못 올라왔으면 커튼만 걷어도 빈 화면이 남으므로 `LoadScene(GameScene)`으로
  떨어뜨린다 — §2의 부팅 스파이크는 노출되지만 플레이는 된다.
- 진입 즉시 `lifetimeCts.Cancel()`로 남은 진행을 끊는다. 타임아웃은 내부 태스크를 취소하지 않고
  기다리기만 멈추므로, 안 끊으면 같은 씬을 두 번 언로드할 수 있다.

**2) `SceneWorkflow.md` §1 "정본 씬 2개" 충돌 — 문서 갱신으로 해소.** §5.3-1 참고.

**3) `SystemMap.md`의 `IsTitleScene` 서술이 낡음 — 갱신.** "씬 문맥이 필요하면 이 값을 쓴다"는
서술이 이번 PR이 고친 것과 같은 버그를 유도한다. 두 판정(`IsTitleScene`/`IsGameplayScene`)이
**서로의 부정이 아니라는 것**(로딩 중에는 둘 다 거짓)을 §2에 명시했다.

**4) `CombatMapInitializer.IsInitialized`를 `SystemMap.md` §2에 등재.** 리뷰는 이 값이 이미 §2에
있는 공개 API라고 했지만 **실제로는 등재된 적이 없었다**(§2 Run/Seed에 있던 것은
`InitializeCombatMap(int)`/`UsedSeed`뿐). `LoadingFlow`가 준비 완료 판정 근거로 쓰면서 통합 계약이
됐으므로 이번에 추가했다 — 초기화 시점을 옮기면 커튼이 준비 전에 걷힌다.

**5) 리뷰가 제안한 WL-212는 등재하지 않는다.** 지적된 두 문서 불일치를 이 PR에서 함께 해소했으므로
`WatchList.md`(**미해소** 항목 대장)의 조건을 만족하지 않는다.

> **리뷰의 사실 오류 2건**(기록만 남긴다)
> - `LoadingFlow.cs:296-299, 326-332, 437-472` · `LoadingScreen.cs:557-558` · `BootWarmup.cs:206-214`로
>   인용된 위치는 **전부 파일 범위 밖**이다(각각 244 · 113 · 114줄). 지적 내용 자체는 정확하므로
>   diff 오프셋을 파일 라인으로 적은 것으로 보인다.
> - `CombatMapInitializer.IsInitialized`가 `SystemMap.md:94`에 등재돼 있다는 서술은 사실이 아니다(위 4번).
>
> 나머지 인용(`SceneWorkflow.md:19`, `SystemMap.md:83`, `LocalizationHelper.cs:45-50`,
> `DataTableManager`의 static 생성자가 4종을 한 번에 적재, GDD에 "로딩" 언급 0건)은 재확인 결과 정확했다.
> **`BuildingFeedback`이 4번째 사례가 아니라는 판정도 맞다** — `!IsTitleScene`을 쓰지만
> `DontDestroyOnLoad`도 latch도 없고, `OnEnable` 1회 검사라 오히려 `IsGameplayScene`으로 바꾸면
> 경고가 영구 소실된다(근거는 `SystemMap.md` §1 표 아래).

### 5.5 미결로 남은 것

| 항목 | 상태 |
| --- | --- |
| 커튼 전환 연출(셀 와이프) | **보류** — 시제작 후 롤백. 확인된 제약과 재개 비용은 §5.4 말미 |
| `marshie-run-v1.anim` 커브 리타겟 커밋 | **다른 저장소 대기** — `NorthLand-Imported`(`@NorthLand/UI/`) 쪽에서 별도 커밋이 필요하다. 이 저장소만으로는 마스코트가 안 뜬다(§5.5 마스코트 항목) |
| TMP 글리프 프리워밍 | JP 정적 서브셋 재베이크(Phase 2 폰트 작업)와 범위가 겹쳐 함께 결정 |
| `BuffBurnReward.asset` 고아 | 담당자 확인 대기(§3.3) |
| Standalone 스크립팅 백엔드 | 눈으로 확인 필요(§4.3) |

#### 마스코트 표시 문제 — **해소**(2026-08-25, 안 가 채택)

**증상(Phase 1 이전부터)**: `marshie-run-v1_0`이 `SpriteRenderer`인데 Screen Space - Overlay Canvas의
자식이라 화면에 **불투명 검정 `Panel`만 보이고 마스코트가 보이지 않았다.** 원인이 두 겹이었다 —
(1) 월드 좌표 `(399, 224.5, 0)`가 원근 카메라 `(0, 1, −10)`의 프러스텀 밖, (2) 보이더라도 Overlay
캔버스가 모든 카메라 렌더 뒤에 그려져 전체 화면 `Panel`이 덮음. 덤으로 월드 스프라이트는
`CanvasGroup` 페이드에도 걸리지 않았다.

**조치(안 가)**: 오브젝트를 UI로 바꾸고 클립을 리바인딩했다.

| 대상 | 변경 |
| --- | --- |
| `LoadingScene`의 `marshie-run-v1_0` | `SpriteRenderer` 제거 → `RectTransform` + `CanvasRenderer` + `Image`. 스프라이트 승계, `raycastTarget=false`(장식), `preserveAspect=true`, 앵커·피벗 중앙, `sizeDelta=(204.8, 204.8)`, `localScale=1`, 레이어 `UI` |
| `Assets/Imported/@NorthLand/UI/marshie-run-v1.anim` | 오브젝트 참조 커브 **25키**를 `SpriteRenderer.m_Sprite` → `Image.m_Sprite`로 이동(구 바인딩 제거). 커브 path는 `''` 그대로 |

> `Assets/Imported`는 별도 저장소(`muchan918/NorthLand-Imported`)이고 CLAUDE.md가 편집을 금하지만,
> `@NorthLand/`는 팀 자체 아트 네임스페이스이고 **이 클립·컨트롤러는 해당 저장소에서 아직 커밋되지
> 않은 신규 파일**이었다(로딩 화면용으로 막 만든 것). 금지 규칙이 겨냥하는 "벤더링된 외부 에셋"에
> 해당하지 않아 진행했다. **커밋은 그 저장소 쪽에서 따로 해야 한다.**

**검증(2026-08-25)**: 플레이 중 `Image.sprite`가 `marshie-run-v1_0` → `marshie-run-v1_23`으로 바뀌는 것을
확인 — 애니메이터가 `Image`를 구동한다. 커튼 캡처에서 검정 배경 위 마스코트가 중앙에 렌더된다.

> ⚠ **`unity-cli screenshot --view game`은 Screen Space - Overlay 캔버스를 잡지 못한다**(카메라 렌더라
> Overlay 합성 단계를 지나지 않는다). 편집·플레이 모드 모두 스카이박스만 찍힌다. 로딩 커튼을 캡처로
> 확인하려면 진단 목적으로 잠시 `ScreenSpaceCamera`로 바꿔 찍어야 한다 — 실제 플레이어에서는 Overlay가
> 플레이어 루프에서 합성되므로 정상 표시된다. **커튼이 안 보인다고 오판하지 말 것.**


### 5.3 ⚠ 같이 따라오는 것 — 착수 전 반드시 확인

1. ~~**`Docs/Core/SceneWorkflow.md` §1과 충돌한다.**~~ → **해소**(2026-08-26): 해당 문서를
   "정본 씬 **세 개**"로 갱신하고, `LoadingScene`이 §3~§5의 **병합 절차 밖**인 이유(단일 소유자·
   소규모라 동시 편집 문제가 성립하지 않음)를 §1-1로 명시했다. `SystemMap.md`의
   `GameSceneManager` 행과 씬 판정 API 서술도 함께 갱신했다.
   ⚠ **팀 계약 변경이므로 리뷰에서 확인받아야 한다** — 문서만 맞춰 둔 상태다.

2. **`IsTitleScene` 경고 latch가 깨진다 — 실제 회귀다.** 아래 셋은
   `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` + `DontDestroyOnLoad`라 **모든 씬에 상주**하며
   `LateUpdate`마다 `MouseManager`를 찾는다.

   - `OutlineInteractionDriver.cs:89`
   - `ResidentDragCoordinator.cs:146`
   - `ResidentSelectionCoordinator.cs:179`

   셋 다 가드가 `!GameSceneManager.IsTitleScene`("타이틀이 아니면 게임플레이다")이다. LoadingScene에는
   `MouseManager`가 없는데 이 가드는 통과하므로 **경고가 발화하고 `_warnedNoMouseManager`가 latch된다.**
   이 플래그는 어디서도 리셋되지 않는다(`HandleSceneLoaded`는 참조만 비운다). 결과적으로
   **LoadingScene이 일회성 경고를 태워 버려, GameScene에서 실제로 `MouseManager`가 없을 때 진단이
   조용해진다.** 가드를 "타이틀 씬이 아님" → "게임플레이 씬이 아님"으로 바꿔야 한다(WL-145 계열).

3. ~~`Assets/Scenes/LoadingScene.unity`는 git 미추적 + Build Settings 미등록 상태다.~~
   → **해소**: Phase 1에서 `EditorBuildSettings`에 TitleScene 바로 뒤로 등재했다. 씬 파일은 아직
   커밋 전이므로, 커밋 시 `.unity`와 `.unity.meta`를 **반드시 함께** 넣는다.

---

## 6. 재측정 절차

에디터가 열려 있어야 한다(`unity-cli status`로 확인). **`editor play`/`profiler`는 사용자 요청 시에만
실행한다**(`Docs/Tools/unity-cli-guide.md` 규칙 A8).

### 6.1 편집 모드 — 에셋 로드 비용

`unity-cli exec`에 `Stopwatch` 측정 코드를 stdin으로 넘긴다(규칙 A7). 측정 본문 예:

```csharp
var sw = new System.Diagnostics.Stopwatch();
sw.Restart();
var a = Resources.LoadAll<TowerAsset>("ScriptableObjects/Towers");
sw.Stop();
return $"{sw.Elapsed.TotalMilliseconds:F2} ms / {a.Length}개";
```

> ⚠ **콜드 측정은 도메인 리로드 직후 1회만 유효하다.** 같은 세션에서 두 번째 호출은 웜(0.68ms)이
> 나온다. 반드시 **재측정 대상을 가장 먼저** 부를 것.

### 6.2 플레이 모드 — 부팅 프레임

```bash
unity-cli profiler clear && unity-cli profiler enable
unity-cli editor play --wait && unity-cli editor stop   # 한 줄로 붙여 즉시 정지
unity-cli profiler hierarchy --frame 0 --depth 3 --min 5.0
unity-cli profiler hierarchy --frame 0 --root ScriptRunDelayedStartupFrame --depth 5 --min 2.0
unity-cli profiler hierarchy --frame 0 --root DoRenderLoop --depth 6 --min 1.0
unity-cli profiler disable
```

> ⚠ **`editor play --wait` 뒤에 곧바로 `stop`을 붙일 것.** 플레이를 놔두면 프로파일러 링버퍼가
> 돌아 **프레임 0이 밀려난다**(실제로 1483프레임까지 가서 유효 범위가 `[325..2324]`로 이동했다).

### 6.3 씬 참조 여부 세기

```bash
for m in Assets/Resources/ScriptableObjects/Towers/*.asset.meta; do
  g=$(grep -m1 "guid:" "$m" | awk '{print $2}')
  grep -q "$g" Assets/Scenes/GameScene.unity || echo "미참조: $(basename "${m%.meta}")"
done
```

---

## 7. 측정 이력

| 날짜 | 내용 | 결과 |
| --- | --- | --- |
| 2026-08-25 | 에디터 편집 모드 — CSV·`Resources.LoadAll` | §3 |
| 2026-08-25 | 에디터 플레이 모드 — 부팅 프레임 프로파일(2회) | §2, 프레임 0 = 976.77ms |
| — | **빌드 실측** | **TODO — 미실시**(§1) |
