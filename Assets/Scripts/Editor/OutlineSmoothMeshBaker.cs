using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// #213 아웃라인용 스무스 노멀 프리베이크(Docs/Core/InteractionOutline.md §6.4).
// 대상 프리팹의 메시를 훑어 "정점 위치를 공유하는 노멀을 평균한 사본"을 에셋으로 굽고,
// 원본→사본 매핑을 OutlineSmoothMeshRegistry에 기록한다.
//
// 에디터에서만 도는 이유: (1) 대상 메시 대부분이 isReadable=false라 런타임엔 정점을 못 읽는다(에디터는 읽힌다),
// (2) FlatKit의 MeshSmoother가 Editor 전용 asmdef다. 벤더 트리(Sweet_Land·TARBO)는 건드리지 않고
// @NorthLand 아래에 사본만 만든다.
public static class OutlineSmoothMeshBaker
{
    private const string k_RegistryPath = "Assets/Resources/Outline/OutlineSmoothMeshRegistry.asset";

    // 산출물은 Assets/Imported 밖에 둔다 — Imported는 프로젝트 저장소에서 .gitignore되고(중첩 git 저장소로 별도 관리)
    // 그 안에 구우면 팀원이 프로젝트를 받아도 사본이 따라오지 않아 레지스트리 참조가 깨진다.
    private const string k_OutputFolder = "Assets/Meshes/OutlineSmooth";

    // 자동 수집 대상 폴더. 타워(배치물)와 영지 섬/산이 아웃라인 대상이다.
    // 건물은 렌더러가 수백 개라 프록시 실루엣을 쓰므로(§6.2) 베이크 대상이 아니다.
    private static readonly string[] k_TargetFolders =
    {
        "Assets/Imported/@NorthLand/Prefabs/Tower",
        "Assets/Imported/@NorthLand/Prefabs/Territory",
    };

    // 대상이 아닌 프리팹 이름 조각: 배치 고스트(선택/호버 대상 아님)와 발사체.
    private static readonly string[] k_NameExcludes = { "ghost", "bullet", "arrow" };

    [MenuItem("NorthLand/Outline/1. 베이크 대상 자동 수집", priority = 100)]
    public static void CollectTargets()
    {
        var registry = LoadOrCreateRegistry();

        var guids = AssetDatabase.FindAssets("t:Prefab", k_TargetFolders);
        var collected = new List<GameObject>();
        var skipped = new List<string>();

        foreach (var guid in guids.Distinct())
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            string lower = prefab.name.ToLowerInvariant();
            if (k_NameExcludes.Any(x => lower.Contains(x))) { skipped.Add($"{prefab.name}(이름 제외)"); continue; }
            if (CollectMeshes(prefab).Count == 0) { skipped.Add($"{prefab.name}(메시 없음)"); continue; }

            collected.Add(prefab);
        }

        collected.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        registry.TargetPrefabs = collected;
        EditorUtility.SetDirty(registry);
        // SaveAssets()는 무관한 더티 에셋(동적 폰트 아틀라스 등)까지 디스크에 써서 남의 작업 트리를 더럽힌다 → 레지스트리만 저장.
        AssetDatabase.SaveAssetIfDirty(registry);

        Debug.Log($"[아웃라인] 베이크 대상 {collected.Count}개 수집: {string.Join(", ", collected.Select(p => p.name))}" +
                  (skipped.Count > 0 ? $"\n제외 {skipped.Count}개: {string.Join(", ", skipped)}" : ""), registry);
    }

    [MenuItem("NorthLand/Outline/2. 스무스 메시 베이크", priority = 101)]
    public static void Bake()
    {
        var registry = LoadOrCreateRegistry();
        if (registry.TargetPrefabs.Count == 0)
        {
            Debug.LogWarning("[아웃라인] 베이크 대상이 비어 있습니다. 먼저 'NorthLand/Outline/1. 베이크 대상 자동 수집'을 실행하세요.", registry);
            return;
        }

        EnsureFolder(k_OutputFolder);

        // 이미 살아 있는 사본은 재사용한다(재실행이 멱등이 되도록).
        var existing = new Dictionary<Mesh, Mesh>();
        foreach (var e in registry.Entries)
        {
            if (e?.Source == null || e.Smooth == null) continue;
            existing[e.Source] = e.Smooth;
        }

        var entries = new List<OutlineSmoothMeshRegistry.Entry>();
        var seen = new HashSet<Mesh>();
        int baked = 0, reused = 0, failed = 0;

        foreach (var prefab in registry.TargetPrefabs)
        {
            if (prefab == null) continue;

            foreach (var source in CollectMeshes(prefab))
            {
                if (!seen.Add(source)) continue; // 여러 프리팹이 같은 메시를 공유한다

                if (existing.TryGetValue(source, out var alive))
                {
                    entries.Add(new OutlineSmoothMeshRegistry.Entry { Source = source, Smooth = alive });
                    reused++;
                    continue;
                }

                var smooth = BakeOne(source);
                if (smooth == null) { failed++; continue; }

                entries.Add(new OutlineSmoothMeshRegistry.Entry { Source = source, Smooth = smooth });
                baked++;
            }
        }

        registry.Entries = entries;
        registry.InvalidateLookup();
        EditorUtility.SetDirty(registry);
        // SaveAssets()는 무관한 더티 에셋(동적 폰트 아틀라스 등)까지 디스크에 써서 남의 작업 트리를 더럽힌다 → 레지스트리만 저장.
        // 사본 메시는 CreateAsset 시점에 이미 디스크에 기록된다.
        AssetDatabase.SaveAssetIfDirty(registry);

        Debug.Log($"[아웃라인] 스무스 메시 베이크 완료 — 신규 {baked} / 재사용 {reused} / 실패 {failed} " +
                  $"(대상 프리팹 {registry.TargetPrefabs.Count}개, 총 메시 {entries.Count}개) → {k_OutputFolder}", registry);
    }

    // 프리팹 트리의 렌더 메시를 모은다(비활성 포함). 아웃라인 대상은 MeshRenderer/SkinnedMeshRenderer뿐이라
    // Line/Trail/Particle 렌더러는 타입상 자동 배제된다.
    private static List<Mesh> CollectMeshes(GameObject prefab)
    {
        var result = new List<Mesh>();

        foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh != null && f.sharedMesh.vertexCount > 0) result.Add(f.sharedMesh);
        }
        foreach (var s in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (s.sharedMesh != null && s.sharedMesh.vertexCount > 0) result.Add(s.sharedMesh);
        }
        return result;
    }

    private static Mesh BakeOne(Mesh source)
    {
        // 에디터에서는 isReadable=false 메시도 정점을 읽을 수 있다(런타임과 다른 점 — §6.4).
        Mesh clone;
        try
        {
            clone = Object.Instantiate(source); // 블렌드셰이프·본 웨이트까지 복사된다(영지 산이 스킨드다)
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[아웃라인] 메시 사본 생성 실패: {source.name} — {ex.GetType().Name}");
            return null;
        }

        clone.name = source.name + "_smooth";

        try
        {
            // 평균 노멀을 uv3(TEXCOORD2)에 기록한다. FlatKit 인스펙터 UI는 멀티 서브메시를 거부하지만
            // uv3만 채우는 이 경로는 서브메시 수와 무관하다(2-서브메시 메시에서 검증).
            FlatKit.MeshSmoother.SmoothNormals(clone);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[아웃라인] 스무스 노멀 생성 실패: {source.name} — {ex.GetType().Name}: {ex.Message}");
            Object.DestroyImmediate(clone);
            return null;
        }

        string path = AssetDatabase.GenerateUniqueAssetPath($"{k_OutputFolder}/{SanitizeFileName(source.name)}_smooth.asset");
        AssetDatabase.CreateAsset(clone, path);
        return AssetDatabase.LoadAssetAtPath<Mesh>(path);
    }

    private static OutlineSmoothMeshRegistry LoadOrCreateRegistry()
    {
        var registry = AssetDatabase.LoadAssetAtPath<OutlineSmoothMeshRegistry>(k_RegistryPath);
        if (registry != null) return registry;

        EnsureFolder(System.IO.Path.GetDirectoryName(k_RegistryPath).Replace('\\', '/'));
        registry = ScriptableObject.CreateInstance<OutlineSmoothMeshRegistry>();
        AssetDatabase.CreateAsset(registry, k_RegistryPath);
        Debug.Log($"[아웃라인] 레지스트리를 새로 만들었습니다: {k_RegistryPath}", registry);
        return registry;
    }

    // "Assets/A/B/C" 형태를 위에서부터 하나씩 만든다(AssetDatabase.CreateFolder는 부모가 있어야 한다).
    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        var parts = folder.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}
