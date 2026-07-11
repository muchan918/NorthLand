# Unity CLI 운용 가이드 (AI 에이전트용)

> **대상**: 이 프로젝트에서 작업하는 모든 AI 에이전트
> **목적**: unity-cli로 Unity Editor를 제어하는 표준 절차를 정의한다. C# 코드·씬·에셋을 수정하는 모든 작업은 이 문서의 워크플로우를 따른다.
> **도구**: [youngwoocho02/unity-cli](https://github.com/youngwoocho02/unity-cli) — Unity Editor와 HTTP로 직접 통신하는 단일 바이너리 CLI. **Unity Editor가 열려 있어야 동작한다.**
> **우선순위**: 이 문서와 실제 CLI 동작이 다르면 `unity-cli <command> --help` 출력이 우선이다. 도구 목록은 이 문서보다 `unity-cli list` 출력이 우선이다.

---

## 0. 절대 규칙

### ALWAYS

1. **세션 시작 시 `unity-cli status` 먼저.** 연결 실패 시 에디터 실행 여부를 사용자에게 확인하고, 해결 전까지 Unity 관련 작업을 진행하지 않는다.
2. **`.cs` 파일을 수정했다면**: `unity-cli editor refresh --compile` → `unity-cli console --type error` 순으로 실행한다. **컴파일 에러가 0이 될 때까지 다음 작업으로 넘어가지 않는다.**
3. **`.prefab` / `.unity` / `.asset` / `.mat` 파일을 텍스트로 편집했다면** 반드시 직후에 `unity-cli reserialize <경로>`를 실행한다. 이 단계를 생략하면 에셋이 조용히 깨질 수 있다.
4. **exec로 씬/에셋을 변경하는 코드에는 저장 처리를 반드시 포함한다.** (§5.2 저장 시맨틱)
5. **10개 이상 파일·오브젝트를 건드리는 일괄 작업 전에는 git 커밋 상태를 사용자에게 확인받는다.** exec와 텍스트 편집은 **Undo 스택을 타지 않는다** — git이 유일한 되돌리기 수단이다.
6. **"완료" 보고 전에 검증 명령을 실행한다.** (console / test / screenshot 중 해당되는 것) 검증 출력이 보고의 근거여야 한다. 추측으로 완료를 주장하지 않는다.
7. **2줄 이상의 C# 코드는 stdin 파이프로 exec에 전달한다.** 인라인 문자열은 셸 이스케이프로 깨지기 쉽다.

### NEVER

1. 에디터 스크립트에서 프리팹 배치에 `Object.Instantiate`를 쓰지 않는다 → `PrefabUtility.InstantiatePrefab` 사용. (프리팹 연결이 끊긴 일반 오브젝트가 된다)
2. `.shadergraph` 파일을 직접 편집하지 않는다. (프로그래밍용 공개 API 없음, JSON 포맷 비공식·버전 의존) → §4.G의 우회 경로 사용.
3. 씬/프리팹 YAML에 **새** GameObject·Component 블록을 텍스트로 추가하지 않는다. (fileID/GUID 수동 발급은 reserialize를 통과하고도 missing reference로 조용히 깨진다) → 구조 변경은 exec로.
4. 플레이 모드 중 `editor refresh`를 실행하지 않는다. (`--force`는 사용자가 명시적으로 요청한 경우에만)
5. exec에 async / 코루틴 / 지연 콜백 코드를 넣지 않는다. (기본 차단됨. 지연 완료가 의도된 경우에만 `--allow-async` + 사유를 사용자에게 설명)
6. 에셋 삭제, 전체 덮어쓰기 등 파괴적 일괄 작업을 사용자 확인 없이 실행하지 않는다.

---

## 1. 도구 선택 결정표

작업을 시작하기 전에 이 표로 경로를 결정한다.

| 하려는 일 | 사용 경로 |
|---|---|
| 에디터/씬 상태 **조회**, 일회성 조작 | `exec` |
| 기존 에셋의 **값만** 변경 (수치 튜닝, 프로퍼티 수정) | 텍스트 편집 → `reserialize` (git diff가 깨끗함) |
| **구조** 변경 (컴포넌트 추가/삭제, 레퍼런스 와이어링, 새 오브젝트) | `exec` |
| 같은 exec 패턴을 3회 이상 반복할 것 같을 때 | `[UnityCliTool]` 커스텀 툴로 승격 (§6) |
| 씬에 프리팹 대량 배치 | `exec` + `PrefabUtility.InstantiatePrefab` |
| C# 수정 후 컴파일 검증 | `editor refresh --compile` + `console --type error` |
| 기능 동작 검증 | `test` (EditMode/PlayMode) |
| 시각 결과 확인 (UI, 이펙트, 배치) | `screenshot` |
| 프레임 비용/병목 분석 | `profiler` |
| 사용 가능한 도구 파악 | `list` |

---

## 2. 표준 작업 루프

모든 Unity 작업은 이 루프를 따른다:

```
status 확인
  → 작업 실행 (exec 또는 파일 편집)
  → 후처리 (.cs 수정 시 refresh --compile / YAML 편집 시 reserialize)
  → 검증 (console --type error → 필요 시 test / screenshot)
  → 저장 확인 (§5.2)
  → 검증 출력을 근거로 보고
```

운용 참고:

- CLI는 명령 전송 전 Unity 상태를 자동 확인하고, 컴파일/도메인 리로드 중이면 응답 가능해질 때까지 대기한다. 재연결 로직을 직접 구현할 필요 없다.
- 오래 걸리는 일괄 작업은 `--timeout <ms>`를 늘린다. (기본 120000ms)
- Unity 인스턴스가 여러 개 열려 있으면 `--project <경로>`로 대상을 지정한다. 기본은 현재 작업 디렉토리의 프로젝트.

---

## 3. 명령 레퍼런스

### 3.1 `status` — 연결/상태 확인

```bash
unity-cli status
# Unity: ready / Project 경로 / Unity 버전 / PID 출력
```

**사용 시점**: 세션 시작, 장시간 대기 후, 명령이 이유 없이 실패할 때 가장 먼저.

### 3.2 `editor` — 플레이 모드 / 리프레시 / 컴파일

```bash
unity-cli editor play --wait      # 플레이 모드 진입, 완전 로드까지 대기 (--wait 항상 권장)
unity-cli editor stop             # 플레이 모드 종료
unity-cli editor pause            # 일시정지 토글 (플레이 모드 중에만)
unity-cli editor refresh          # 에셋 리프레시 (플레이 모드 중엔 차단)
unity-cli editor refresh --compile  # 리프레시 + 스크립트 재컴파일 ← .cs 수정 후 필수
```

**사용 시점**: `.cs` 수정 후 `refresh --compile`은 필수 절차. 플레이 테스트는 반드시 `--wait`로 진입 완료를 보장한 뒤 다음 명령 실행.

### 3.3 `console` — 로그 읽기 (핵심 검증 도구)

```bash
unity-cli console                          # 에러+경고 (기본)
unity-cli console --type error             # 에러만
unity-cli console --type error,warning,log --lines 20
unity-cli console --stacktrace user        # 유저 코드 스택트레이스 포함 (디버깅 시)
unity-cli console --clear                  # 콘솔 비우기
```

**사용 시점**: 모든 변경 작업 직후. **패턴**: 작업 전 `--clear`로 비우고 → 작업 → `console --type error`로 새 에러만 확인하면 노이즈가 없다.

### 3.4 `exec` — 임의 C# 실행 ★ 가장 중요

Unity Editor 런타임 안에서 C# 코드를 컴파일·실행한다. UnityEngine, UnityEditor, 로드된 모든 어셈블리(ECS 포함)에 접근 가능. 에디터 스크립팅으로 가능한 모든 것을 프로젝트 재컴파일 없이 즉석 실행하는 것과 같다.

```bash
# 한 줄 조회
unity-cli exec "return EditorSceneManager.GetActiveScene().name;"

# 2줄 이상은 반드시 stdin 파이프 (규칙 A7)
echo '
var go = new GameObject("Marker");
go.tag = "EditorOnly";
return go.name;' | unity-cli exec

# 프로젝트 고유 네임스페이스는 --usings (반복 지정 가능)
unity-cli exec "return World.All.Count;" --usings Unity.Entities
```

규칙:
- `return`으로 결과를 받는다. 흔한 네임스페이스(UnityEngine, UnityEditor 등)는 기본 포함.
- 컴파일 에러가 반환되면 **먼저 `--usings` 누락을 의심**하고, 다음으로 API 오타를 확인한다.
- 씬을 바꿨으면 씬 저장, 에셋을 바꿨으면 에셋 저장 코드를 같은 exec 안에 포함한다. (§5.2)
- async/코루틴은 기본 차단 (규칙 N5).

### 3.5 `test` — Unity Test Framework 실행

```bash
unity-cli test                          # EditMode 테스트 (기본)
unity-cli test --mode PlayMode          # PlayMode 테스트 (도메인 리로드 발생, CLI가 자동 폴링)
unity-cli test --filter MyTestClass     # 이름 부분 일치 필터
```

**사용 시점**: 기능 구현·수정 완료 후, 리팩토링 후. 테스트가 존재하는 영역을 수정했다면 실행이 기본값이다.

### 3.6 `menu` — 메뉴 아이템 실행

```bash
unity-cli menu "File/Save Project"
unity-cli menu "Assets/Refresh"
unity-cli menu "Window/General/Console"
```

**사용 시점**: 전용 명령이 없는 에디터 기능, 프로젝트에 등록된 커스텀 MenuItem 실행. `File/Quit`은 안전상 차단됨.

### 3.7 `reserialize` — 텍스트 편집한 에셋 정상화

```bash
unity-cli reserialize Assets/Prefabs/Player.prefab              # 단일
unity-cli reserialize Assets/Scenes/Main.unity Assets/Scenes/Lobby.unity  # 복수
unity-cli reserialize                                           # 프로젝트 전체 (오래 걸림, 사용자 확인 후)
```

**동작**: 에셋을 Unity가 메모리로 로드했다가 자체 시리얼라이저로 다시 기록 → 인스펙터로 편집한 것과 같은 유효한 YAML이 된다.
**사용 시점**: `.prefab` `.unity` `.asset` `.mat`을 텍스트로 수정한 **직후 반드시** (규칙 A3).

### 3.8 `screenshot` — 씬/게임 뷰 캡처

```bash
unity-cli screenshot        # PNG 캡처. 저장 경로는 명령 출력 확인. 옵션: --help
```

**사용 시점**: UI 배치, 이펙트, 오브젝트 배치 등 시각 결과 검증. **피드백 루프**: 수정 → screenshot → 이미지 확인 → 재수정. 단, UI 레이아웃 캡처 전 §5.5 참고.

### 3.9 `profiler` — 성능 분석

```bash
unity-cli profiler enable                          # 기록 시작
unity-cli editor play --wait                       # 측정 대상 실행
unity-cli profiler hierarchy --frames 30 --min 0.5 # 최근 30프레임 평균, 0.5ms 이상만
unity-cli profiler hierarchy --root SimulationSystem --depth 3  # 특정 시스템 드릴다운
unity-cli profiler hierarchy --sort self --max 10  # self time 상위 10개
unity-cli profiler disable && unity-cli profiler clear
```

**사용 시점**: "느리다"는 이슈 조사, 최적화 전후 비교. **원칙**: 최적화 전에 반드시 측정으로 병목을 특정하고, 최적화 후 같은 조건으로 재측정해 수치로 보고한다.

### 3.10 `list` — 도구 발견

```bash
unity-cli list    # 내장 + 이 프로젝트의 커스텀 툴을 파라미터 스키마와 함께 표시
```

**사용 시점**: 세션 초반 1회 실행해 이 프로젝트에 등록된 커스텀 툴을 파악한다. 커스텀 툴이 있는 작업은 exec보다 커스텀 툴을 우선 사용한다.

```bash
# 커스텀 툴 호출
unity-cli <tool_name> --params '{"key": "value"}'
unity-cli <tool_name> --key value
```

---

## 4. 상황별 플레이북

### A. C# 스크립트 수정 후 (가장 빈번한 루프)

```bash
unity-cli console --clear
unity-cli editor refresh --compile
unity-cli console --type error
# 에러 있으면: 수정 → 이 루프 반복. 에러 0 확인 후에만 다음 단계.
unity-cli test --filter <관련테스트>   # 해당 영역에 테스트가 있으면
```

### B. UI 생성·와이어링

인스펙터 드래그앤드롭을 코드로 대체한다. 핵심 API 두 개:
- `UnityEditor.Events.UnityEventTools.AddPersistentListener` — 직렬화되는 **영구** 리스너 (런타임 `AddListener`와 다름. 씬에 저장됨)
- `SerializedObject` / `SerializedProperty` — 인스펙터 레퍼런스 필드 주입

```bash
echo '
var mgr = Object.FindFirstObjectByType<UIManager>();
foreach (var btn in Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None))
    UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, mgr.OnAnyButton);

var so = new SerializedObject(mgr);
so.FindProperty("rootPanel").objectReferenceValue = GameObject.Find("Canvas/Root");
so.ApplyModifiedProperties();

EditorSceneManager.MarkAllScenesDirty();
EditorSceneManager.SaveOpenScenes();
return "wired";' | unity-cli exec
```

검증: `screenshot`으로 배치 확인 + 플레이 모드 스모크 테스트(§I). 픽셀 단위 레이아웃 폴리싱은 근사까지만 하고 최종 판단은 사용자에게 넘긴다.

### C. 프리팹 일괄 작업 (컴포넌트 추가/구조 변경)

`LoadPrefabContents → 수정 → SaveAsPrefabAsset → UnloadPrefabContents` 패턴 고정:

```bash
echo '
int count = 0;
foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[]{ "Assets/Prefabs" })) {
    var path = AssetDatabase.GUIDToAssetPath(guid);
    var root = PrefabUtility.LoadPrefabContents(path);
    if (root.GetComponent<AudioSource>() == null) {
        root.AddComponent<AudioSource>();
        PrefabUtility.SaveAsPrefabAsset(root, path);
        count++;
    }
    PrefabUtility.UnloadPrefabContents(root);
}
return $"modified {count} prefabs";' | unity-cli exec
```

사전 확인: 대상 개수를 먼저 조회(`return AssetDatabase.FindAssets(...).Length;`)해서 사용자에게 규모를 보고하고 git 커밋 확인 (규칙 A5).

### D. 씬 오브젝트 대량 배치

```bash
echo '
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tree.prefab");
for (int x = 0; x < 10; x++)
for (int z = 0; z < 10; z++) {
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);   // Instantiate 금지!
    go.transform.position = new Vector3(x * 2f, 0f, z * 2f);
}
EditorSceneManager.MarkAllScenesDirty();
EditorSceneManager.SaveOpenScenes();
return "placed 100";' | unity-cli exec
```

검증: `screenshot`. 지면 스냅이 필요하면 `Physics.Raycast`로 y를 보정.

### E. 에셋 값 튜닝 (YAML 텍스트 편집 경로)

**허용 범위**: 기존 필드의 값 변경만. (수치, 이름, enum 값, 기존 레퍼런스의 대상 교체 등)

```bash
# 1. .mat / .prefab / .asset 파일을 텍스트로 편집 (기존 값만 수정)
# 2. 직후 반드시:
unity-cli reserialize Assets/Materials/Character.mat
# 3. 검증:
unity-cli console --type error
```

**금지 범위**: 새 컴포넌트/오브젝트 블록 추가 (규칙 N3) → exec(§C) 사용.

### F. 포스트 프로세싱 (URP/HDRP Volume)

Volume 시스템은 전부 공개 API. VolumeProfile은 ScriptableObject 에셋이라 일괄 튜닝이 스크립트로 된다:

```bash
echo '
var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/GlobalVolume.asset");
if (!profile.TryGet<Bloom>(out var bloom)) bloom = profile.Add<Bloom>(true);
bloom.intensity.overrideState = true;
bloom.intensity.value = 0.8f;
EditorUtility.SetDirty(profile);
AssetDatabase.SaveAssets();
return "ok";' | unity-cli exec --usings UnityEngine.Rendering --usings UnityEngine.Rendering.Universal
```

(HDRP면 `UnityEngine.Rendering.HighDefinition`으로 교체.) 검증: `screenshot` 전후 비교.

### G. 셰이더 작업

- **Shader Graph(.shadergraph) 수정 요청** → 직접 편집 불가(규칙 N2)를 사용자에게 알리고 대안 제시:
  1. 실체가 "머티리얼 파라미터 변경"이면 → `.mat` 편집+reserialize 또는 exec에서 `material.SetFloat/SetColor` 루프 (쉬움)
  2. 셰이더 로직 자체 작성/수정이면 → **HLSL/ShaderLab 코드 셰이더**로 작성. 텍스트 파일이라 정상 작업 가능하고, 컴파일 에러는 `refresh` 후 `console`로 확인하는 표준 루프가 그대로 돌아간다.

### H. 성능 진단

```bash
unity-cli profiler clear && unity-cli profiler enable
unity-cli editor play --wait
# (재현 시나리오 수행 — 필요 시 exec로 게임 상태 조작)
unity-cli profiler hierarchy --frames 60 --min 1.0 --sort self --max 15
unity-cli editor stop && unity-cli profiler disable
```

보고 형식: "병목 = X (self N ms/frame, 60프레임 평균)" → 수정 → 동일 조건 재측정 → 전후 수치 비교.

### I. 플레이 모드 스모크 테스트

큰 변경 후 최소 검증:

```bash
unity-cli console --clear
unity-cli editor play --wait
unity-cli console --type error,warning --stacktrace user
unity-cli editor stop
```

진입 직후 에러/경고 0이면 통과. 에러가 있으면 stop 후 수정하고 반복.

---

## 5. 함정과 필수 지식

### 5.1 프리팹 연결
에디터 컨텍스트에서 `Object.Instantiate(prefab)`은 연결 끊긴 사본을 만든다. 씬 배치는 항상 `PrefabUtility.InstantiatePrefab`. 프리팹 **에셋 자체** 수정은 `LoadPrefabContents/SaveAsPrefabAsset/UnloadPrefabContents`(§4.C).

### 5.2 저장 시맨틱 (exec에서 가장 흔한 실수)
exec에서 변경해도 저장하지 않으면 에디터 메모리에만 존재한다. 변경 대상에 따라:

```csharp
// 씬 오브젝트를 변경했을 때
EditorSceneManager.MarkAllScenesDirty();
EditorSceneManager.SaveOpenScenes();

// 에셋(ScriptableObject, Material, Prefab 에셋 등)을 변경했을 때
EditorUtility.SetDirty(target);
AssetDatabase.SaveAssets();
```

### 5.3 Undo 없음
exec·텍스트 편집 변경은 에디터 Undo(Ctrl+Z) 대상이 아니다. 되돌리기 = git. 그래서 규칙 A5(일괄 작업 전 커밋 확인)가 존재한다.

### 5.4 exec 비동기 차단
`exec`는 코드 완료 전에 반환되는 경로(async/코루틴/지연 콜백)를 기본 차단한다. 지연 실행이 정말 필요하면 `--allow-async`를 쓰되, 결과 확인이 불가능함을 감안해 이후 `console`로 부작용을 검증한다.

### 5.5 UI 레이아웃 타이밍
LayoutGroup 기반 UI를 exec로 수정한 직후 캡처하면 재배치 전 상태가 찍힐 수 있다. 캡처 전 강제 리빌드:

```csharp
UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
```

### 5.6 fileID / GUID
YAML의 오브젝트 참조는 fileID(파일 내)와 GUID(파일 간, .meta에 정의)로 구성된다. 이 값을 손으로 만들거나 수정하지 않는다. GUID 하나만 틀려도 reserialize를 **통과한 뒤** missing script/reference로 조용히 깨진다.

### 5.7 플레이 모드 상태
- 플레이 모드 중 씬 오브젝트 변경은 stop 시 사라진다. (예외: VolumeProfile 같은 **에셋** 변경은 유지 — 튜닝 루프에 활용 가능)
- 플레이 모드 진입/종료는 도메인 리로드를 유발할 수 있다. CLI가 자동 대기하지만, 상태가 의심되면 `status`.
- `FindFirstObjectByType` / `FindObjectsByType`은 Unity 2023.1+ API. 구버전 프로젝트면 `FindObjectOfType` 계열로 대체.

---

## 6. 커스텀 툴 작성 (반복 패턴의 승격)

같은 exec 패턴을 3회 이상 쓰게 되면 커스텀 툴로 만들어 등록한다. Editor 어셈블리에 static 클래스 + `[UnityCliTool]`:

```csharp
using UnityCliConnector;
using Newtonsoft.Json.Linq;
using UnityEngine;

[UnityCliTool(Name = "spawn", Description = "Spawn a prefab at a position", Group = "gameplay")]
public static class SpawnTool
{
    public class Parameters
    {
        [ToolParameter("X world position", Required = true)] public float X { get; set; }
        [ToolParameter("Z world position", Required = true)] public float Z { get; set; }
        [ToolParameter("Prefab name in Resources", DefaultValue = "Enemy")] public string Prefab { get; set; }
    }

    public static object HandleCommand(JObject parameters)
    {
        var p = new ToolParams(parameters);
        var prefab = Resources.Load<GameObject>(p.Get("prefab", "Enemy"));
        if (prefab == null) return new ErrorResponse("prefab not found");
        var go = Object.Instantiate(prefab, new Vector3(p.GetFloat("x", 0), 0, p.GetFloat("z", 0)), Quaternion.identity);
        return new SuccessResponse("spawned", new { go.name });
    }
}
```

규칙 요약:
- static 클래스 + `public static object HandleCommand(JObject)` (또는 `async Task<object>`)
- 반환은 `SuccessResponse(message, data)` / `ErrorResponse(message)`
- 메인 스레드에서 실행되므로 모든 Unity API 안전
- 도메인 리로드 시 자동 발견. 작성 후 `refresh --compile` → `unity-cli list`로 등록 확인
- `Parameters` 중첩 클래스 + `[ToolParameter]`를 반드시 작성 — `list`가 스키마를 노출해야 다른 에이전트가 발견할 수 있다

호출: `unity-cli spawn --x 1 --z 5 --prefab Goblin`

---

## 7. 이 프로젝트 전용 규칙

> 에이전트는 이 섹션의 내용을 §0~6보다 우선 적용한다.

- 등록된 커스텀 툴: (`unity-cli list`로 확인 — 여기에 주요 툴과 용도를 기록)
- 건드리면 안 되는 에셋/폴더:
- 씬 저장 정책 (자동 저장 허용 여부):
- 테스트 필수 영역:
- 렌더 파이프라인: (URP / HDRP / Built-in)
- Unity 버전:
