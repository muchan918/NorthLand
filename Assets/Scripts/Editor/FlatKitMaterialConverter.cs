using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// #148 전역 비주얼 룩 — 오브젝트 셰이딩을 FlatKit(Stylized Surface)으로 이관하는 에디터 툴.
// 설계 근거: Docs/Rendering/VisualLookPipeline.md §3.4(툰 셰이딩) · §5(에셋 배치)
//
// 왜 "복제 후 스왑"인가: 벤더 원본(Assets/Imported/Candy_Land/Materials 등)은 무수정 규칙이라
// 셰이더를 직접 갈아끼울 수 없다. 원본 1개당 FlatKit 사본 1개를 만들고 렌더러의 머티리얼
// 슬롯만 바꾼다. 원본은 그대로 남아 있으므로 언제든 되돌릴 수 있다(Revert).
//
// 룩 수치의 정본은 템플릿 머티리얼 하나(k_TemplatePath)다. 사본은 템플릿 파라미터를 그대로
// 물려받고 원본에서 알베도·컬러·표면 상태만 승계한다. 그래서 룩을 고칠 때 사본 수백 개를
// 만지지 않고 템플릿만 고친 뒤 "템플릿 재적용"을 돌리면 전체에 퍼진다.
//
// 산출물 배치가 두 저장소로 갈리는 이유:
//  - 사본 머티리얼 → Assets/Imported/@NorthLand (아트 저장소). 아트 프리팹이 참조하므로 같이 따라와야 한다(§5).
//  - 템플릿 + 변환 기록(JSON) → 프로젝트 저장소. 룩 수치와 "무엇을 변환했는지"는 리뷰 대상이고,
//    아트 저장소가 없는 상태에서도 히스토리에 남아야 한다.
public static class FlatKitMaterialConverter
{
    private const string k_ShaderName = "FlatKit/Stylized Surface";

    // 본진 전용이 아니라 프로젝트 전체의 툰 룩 정본이다 — 주민·전투 에셋도 같은 템플릿을 쓴다.
    private const string k_TemplatePath = "Assets/Settings/FlatKit/FlatKitToon_Template.mat";
    private const string k_MapPath = "Assets/Settings/FlatKit/FlatKitConversion.json";

    // 사본은 벤더 폴더 밖(@NorthLand)에 만든다 — 벤더 트리는 무수정.
    private const string k_OutputFolder = "Assets/Imported/@NorthLand/Materials/FlatKit";

    private const string k_Tag = "[FlatKit]";

    // 원본에서 사본으로 그대로 옮겨야 하는 표면 상태. CandyLand 머티리얼 120개가 전부
    // _Cull = 0(Off, 양면)이라 이걸 빠뜨리면 뒷면이 컬링돼 지붕·간판에 구멍이 난다.
    private static readonly string[] k_SurfaceStateProps =
    {
        "_Surface", "_Blend", "_AlphaClip", "_SrcBlend", "_DstBlend", "_ZWrite", "_Cull", "_Cutoff",
    };

    // 원본에서 승계하는 키워드. 나머지 키워드는 템플릿(룩) 소관이라 건드리지 않는다.
    private static readonly string[] k_CarriedKeywords =
    {
        "_ALPHATEST_ON", "_ALPHAPREMULTIPLY_ON",
    };

    // ── 메뉴 ────────────────────────────────────────────────────────────────

    [MenuItem("NorthLand/FlatKit/1. 선택 항목을 FlatKit으로 변환", priority = 200)]
    private static void ConvertSelectionMenu()
    {
        Debug.Log(Convert(Selection.gameObjects));
    }

    [MenuItem("NorthLand/FlatKit/2. 템플릿 재적용 (룩 튜닝 반영)", priority = 201)]
    private static void ReapplyTemplateMenu()
    {
        Debug.Log(ReapplyTemplate());
    }

    [MenuItem("NorthLand/FlatKit/3. 선택 항목을 원본 머티리얼로 복귀", priority = 202)]
    private static void RevertSelectionMenu()
    {
        Debug.Log(Revert(Selection.gameObjects));
    }

    [MenuItem("NorthLand/FlatKit/4. 원장 점검 (진단만 — 바꾸지 않는다)", priority = 203)]
    private static void AuditMenu()
    {
        Debug.Log(Audit());
    }

    // 정리는 메뉴를 따로 세우고 대화상자로 한 번 더 막는다. `Convert` 안에 있던 같은 삭제가
    // 위험했던 이유는 삭제 자체가 아니라 **아무도 보지 않는 자리에서 일어난다**는 것이었다.
    [MenuItem("NorthLand/FlatKit/5. 끊긴 기록 정리 (사본이 사라진 항목 제거)", priority = 204)]
    private static void PruneMissingMenu()
    {
        ConversionMap map = LoadMap();
        List<ConversionEntry> broken = map.entries.Where(e => LoadByGuid(e.converted) == null).ToList();

        if (broken.Count == 0)
        {
            Debug.Log($"{k_Tag} 정리할 항목 없음 — 기록 {map.entries.Count}건 전부 사본이 살아 있다.");
            return;
        }

        bool ok = EditorUtility.DisplayDialog(
            "FlatKit 원장 정리",
            $"사본을 로드할 수 없는 기록 {broken.Count}건을 원장에서 지운다.\n\n" +
            "⚠ Assets/Imported(중첩 저장소)가 최신인지 먼저 확인할 것 — 낡은 체크아웃에서 실행하면 " +
            "멀쩡한 매핑이 지워지고 원본 복귀(Revert) 경로가 팀 전체에서 끊긴다.\n\n" +
            string.Join("\n", broken.Take(15).Select(e => e.name)) +
            (broken.Count > 15 ? "\n…" : string.Empty),
            "Imported 최신 확인했다 — 정리",
            "취소");

        if (!ok)
        {
            return;
        }

        Debug.Log(PruneMissing());
    }

    // ── 공개 API (unity-cli exec에서 직접 호출한다) ──────────────────────────

    /// <summary>
    /// roots 하위 렌더러의 머티리얼을 FlatKit 사본으로 교체한다.
    /// 이미 사본이 물려 있는 슬롯은 건너뛰므로 여러 번 돌려도 안전하다.
    /// 씬 오브젝트는 dirty 표시만 하고 저장하지 않는다(프로젝트 규칙: 씬 자동 저장 금지).
    /// </summary>
    public static string Convert(IEnumerable<GameObject> roots)
    {
        if (!TryLoadTemplate(out Material template, out string error))
        {
            return error;
        }

        EnsureFolder(k_OutputFolder);

        ConversionMap map = LoadMap();
        map.template = AssetDatabase.AssetPathToGUID(k_TemplatePath);

        Dictionary<string, ConversionEntry> bySource = map.entries
            .GroupBy(e => e.source)
            .ToDictionary(g => g.Key, g => g.First());

        // "이미 우리 사본"인지는 폴더 경로가 아니라 매핑 기록으로 판단한다.
        // 경로로 판단하면 출력 폴더를 옮기는 순간 과거 사본을 못 알아본다.
        var ownedGuids = new HashSet<string>(map.entries.Select(e => e.converted));

        int created = 0;
        int reused = 0;
        int slots = 0;
        int skippedAlready = 0;
        var unresolved = new List<string>();
        var dirtyScenes = new HashSet<UnityEngine.SceneManagement.Scene>();

        foreach (Renderer renderer in CollectRenderers(roots))
        {
            Material[] mats = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                Material src = mats[i];

                if (src == null)
                {
                    continue;
                }

                string srcPath = AssetDatabase.GetAssetPath(src);

                // 에셋이 아닌 인스턴스 머티리얼은 복제 원본으로 삼을 수 없다.
                if (string.IsNullOrEmpty(srcPath))
                {
                    unresolved.Add($"{renderer.name}[{i}] {src.name} (에셋 아님)");
                    continue;
                }

                // 이미 우리 사본이 물려 있으면 건너뛴다(멱등).
                if (ownedGuids.Contains(AssetDatabase.AssetPathToGUID(srcPath)))
                {
                    skippedAlready++;
                    continue;
                }

                Material converted = GetOrCreateConverted(src, srcPath, template, map, bySource, ownedGuids, ref created, ref reused);

                if (converted == null)
                {
                    unresolved.Add($"{renderer.name}[{i}] {src.name} (사본 생성 실패)");
                    continue;
                }

                mats[i] = converted;
                changed = true;
                slots++;
            }

            if (!changed)
            {
                continue;
            }

            renderer.sharedMaterials = mats;
            EditorUtility.SetDirty(renderer);

            if (renderer.gameObject.scene.IsValid())
            {
                dirtyScenes.Add(renderer.gameObject.scene);
            }
        }

        SaveMap(map);

        foreach (UnityEngine.SceneManagement.Scene scene in dirtyScenes)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        string report = $"{k_Tag} 변환 완료 — 사본 신규 {created} / 재사용 {reused} / 슬롯 교체 {slots}" +
                        $" / 이미 변환됨 {skippedAlready}";

        if (unresolved.Count > 0)
        {
            report += $"\n{k_Tag} 처리 못한 슬롯 {unresolved.Count}개: {string.Join(", ", unresolved.Take(10))}" +
                      (unresolved.Count > 10 ? " …" : string.Empty);
        }

        if (dirtyScenes.Count > 0)
        {
            report += $"\n{k_Tag} 씬 {dirtyScenes.Count}개가 dirty 상태다 — 저장은 직접 할 것(자동 저장 안 함).";
        }

        return report;
    }

    /// <summary>
    /// 템플릿의 룩 파라미터를 기존 사본 전체에 다시 퍼뜨린다.
    /// 사본은 템플릿으로 리셋된 뒤 원본에서 알베도·표면 상태만 다시 승계하므로,
    /// 튜닝 반복(§3.4)에서 사본을 하나씩 만질 필요가 없다.
    /// </summary>
    public static string ReapplyTemplate()
    {
        if (!TryLoadTemplate(out Material template, out string error))
        {
            return error;
        }

        ConversionMap map = LoadMap();
        int updated = 0;
        int missing = 0;

        foreach (ConversionEntry entry in map.entries)
        {
            Material src = LoadByGuid(entry.source);
            Material dst = LoadByGuid(entry.converted);

            if (src == null || dst == null)
            {
                missing++;
                continue;
            }

            dst.shader = template.shader;
            dst.CopyPropertiesFromMaterial(template);
            CarryOverFromSource(src, dst);

            EditorUtility.SetDirty(dst);
            AssetDatabase.SaveAssetIfDirty(dst);
            updated++;
        }

        return $"{k_Tag} 템플릿 재적용 — 갱신 {updated}" +
               (missing > 0 ? $" / 참조 끊김 {missing} (원인은 메뉴 「4. 원장 점검」)" : string.Empty);
    }

    /// <summary>
    /// roots 하위 렌더러에 물린 FlatKit 사본을 원본 머티리얼로 되돌린다.
    /// 사본 에셋 자체는 지우지 않는다 — 다시 변환할 때 재사용된다.
    /// </summary>
    public static string Revert(IEnumerable<GameObject> roots)
    {
        ConversionMap map = LoadMap();

        Dictionary<string, string> sourceByConverted = map.entries
            .GroupBy(e => e.converted)
            .ToDictionary(g => g.Key, g => g.First().source);

        int slots = 0;
        int unknown = 0;
        var dirtyScenes = new HashSet<UnityEngine.SceneManagement.Scene>();

        foreach (Renderer renderer in CollectRenderers(roots))
        {
            Material[] mats = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                {
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mats[i]));

                if (string.IsNullOrEmpty(guid) || !sourceByConverted.TryGetValue(guid, out string srcGuid))
                {
                    continue;
                }

                Material src = LoadByGuid(srcGuid);

                if (src == null)
                {
                    unknown++;
                    continue;
                }

                mats[i] = src;
                changed = true;
                slots++;
            }

            if (!changed)
            {
                continue;
            }

            renderer.sharedMaterials = mats;
            EditorUtility.SetDirty(renderer);

            if (renderer.gameObject.scene.IsValid())
            {
                dirtyScenes.Add(renderer.gameObject.scene);
            }
        }

        foreach (UnityEngine.SceneManagement.Scene scene in dirtyScenes)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        return $"{k_Tag} 원본 복귀 — 슬롯 {slots}" + (unknown > 0 ? $" / 원본 유실 {unknown}" : string.Empty) +
               (dirtyScenes.Count > 0 ? $"\n{k_Tag} 씬 {dirtyScenes.Count}개 dirty — 저장은 직접 할 것." : string.Empty);
    }

    /// <summary>
    /// 원장과 실제 에셋이 어긋난 지점을 **바꾸지 않고** 보고한다.
    ///
    /// 두 방향을 함께 보는 이유: 한쪽만 보면 원인을 못 짚는다. 같은 사고가 원장에서는 "기록이 없다"로,
    /// 폴더에서는 "사본이 늘었다"로 나타나기 때문이다.
    ///  - **끊긴 기록** — 원장에 있는데 사본(또는 원본)이 로드되지 않는다. Imported 미동기이거나 실제 삭제다.
    ///  - **미등재 사본** — 출력 폴더에 있는데 원장에 없다. 과거 `Convert`가 기록을 조용히 지우고 새로
    ///    만든 흔적이며, `Revert`가 원장으로 역매핑을 만들기 때문에 **이 사본들은 되돌릴 수 없다**(WL-194).
    ///    그래서 참조 개수까지 함께 센다 — 참조 중인 미등재 사본이 실제 피해이고, 참조 0은 잔재다.
    /// </summary>
    public static string Audit()
    {
        ConversionMap map = LoadMap();

        var broken = new List<string>();

        foreach (ConversionEntry entry in map.entries)
        {
            bool hasCopy = LoadByGuid(entry.converted) != null;
            bool hasSource = LoadByGuid(entry.source) != null;

            if (hasCopy && hasSource)
            {
                continue;
            }

            broken.Add($"{entry.name} (사본 {(hasCopy ? "OK" : "없음")} / 원본 {(hasSource ? "OK" : "없음")})");
        }

        // 폴더가 아예 없으면 Imported 자체가 미동기라 "미등재 사본"을 판단할 근거가 없다 —
        // 여기서 0건이라고 답하면 그게 곧 오진이 된다.
        if (!AssetDatabase.IsValidFolder(k_OutputFolder))
        {
            return $"{k_Tag} 점검 중단 — 출력 폴더가 없다: {k_OutputFolder}. " +
                   $"Assets/Imported(중첩 저장소)를 먼저 동기화할 것(WL-194).\n" +
                   $"{k_Tag} 원장 기록 {map.entries.Count}건 / 끊긴 기록 {broken.Count}건.";
        }

        var listed = new HashSet<string>(map.entries.Select(e => e.converted));
        string[] copyGuids = AssetDatabase.FindAssets("t:Material", new[] { k_OutputFolder });

        var orphanPaths = new List<string>();

        foreach (string guid in copyGuids)
        {
            if (listed.Contains(guid))
            {
                continue;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!string.IsNullOrEmpty(path))
            {
                orphanPaths.Add(path);
            }
        }

        Dictionary<string, int> refs = CountReferences(orphanPaths);

        var report = new System.Text.StringBuilder();
        report.Append($"{k_Tag} 원장 점검 — 기록 {map.entries.Count}건 / 폴더 사본 {copyGuids.Length}개");

        if (broken.Count > 0)
        {
            report.Append($"\n{k_Tag} 끊긴 기록 {broken.Count}건 (메뉴 5로 정리 — Imported 최신 확인 후): ");
            report.Append(string.Join(", ", broken.Take(20)));
            report.Append(broken.Count > 20 ? " …" : string.Empty);
        }

        if (orphanPaths.Count > 0)
        {
            int live = orphanPaths.Count(x => refs.TryGetValue(x, out int c) && c > 0);

            report.Append($"\n{k_Tag} 미등재 사본 {orphanPaths.Count}개 (그중 참조 중 {live}개 — " +
                          "**이 사본들은 Revert로 되돌릴 수 없다**):");

            foreach (string path in orphanPaths.OrderByDescending(x => refs.TryGetValue(x, out int c) ? c : 0))
            {
                int count = refs.TryGetValue(path, out int c2) ? c2 : 0;

                report.Append($"\n  - {System.IO.Path.GetFileNameWithoutExtension(path)}" +
                              (count > 0 ? $" — 참조 {count}곳" : " — 참조 없음(잔재)"));
            }
        }

        if (broken.Count == 0 && orphanPaths.Count == 0)
        {
            report.Append($"\n{k_Tag} 어긋난 지점 없음.");
        }

        return report.ToString();
    }

    /// <summary>
    /// 사본을 로드할 수 없는 기록을 원장에서 제거한다.
    ///
    /// **`Convert`가 하던 조용한 삭제를 그대로 옮겨온 것이다** — 동작은 같고, 사람이 "Imported가
    /// 최신"임을 확인하고 누른다는 점만 다르다. 그 한 가지가 WL-194의 전부다.
    /// </summary>
    public static string PruneMissing()
    {
        ConversionMap map = LoadMap();
        List<ConversionEntry> broken = map.entries.Where(e => LoadByGuid(e.converted) == null).ToList();

        if (broken.Count == 0)
        {
            return $"{k_Tag} 정리할 항목 없음 — 기록 {map.entries.Count}건 전부 사본이 살아 있다.";
        }

        foreach (ConversionEntry entry in broken)
        {
            map.entries.Remove(entry);
        }

        SaveMap(map);

        return $"{k_Tag} 원장 정리 — {broken.Count}건 제거: " +
               string.Join(", ", broken.Take(20).Select(e => e.name)) +
               (broken.Count > 20 ? " …" : string.Empty);
    }

    // ── 내부 ────────────────────────────────────────────────────────────────

    /// 대상 에셋들을 **직접** 참조하는 프리팹·씬의 개수. 역참조 조회 API가 없어 전수 조사로 센다.
    ///
    /// 대상별로 훑지 않고 소비처를 한 번만 훑는다 — 대상이 12개면 전수 조사가 12배가 되므로,
    /// 명시적 진단 메뉴라도 그 비용은 받아들일 이유가 없다.
    private static Dictionary<string, int> CountReferences(IEnumerable<string> targetPaths)
    {
        var counts = new Dictionary<string, int>();

        foreach (string path in targetPaths)
        {
            counts[path] = 0;
        }

        if (counts.Count == 0)
        {
            return counts;
        }

        // `t:Prefab t:Scene`을 한 필터에 넣으면 AND로 해석될 수 있어 따로 돌린다.
        var consumers = new HashSet<string>();

        foreach (string filter in new[] { "t:Prefab", "t:Scene" })
        {
            foreach (string guid in AssetDatabase.FindAssets(filter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (!string.IsNullOrEmpty(path))
                {
                    consumers.Add(path);
                }
            }
        }

        foreach (string consumer in consumers)
        {
            // recursive = false. 머티리얼은 렌더러가 직접 물므로 직접 의존성으로 잡히고,
            // true로 켜면 프리팹 중첩을 타고 들어가 같은 참조가 여러 번 세어진다.
            foreach (string dep in AssetDatabase.GetDependencies(consumer, false))
            {
                if (counts.ContainsKey(dep))
                {
                    counts[dep]++;
                }
            }
        }

        return counts;
    }

    private static Material GetOrCreateConverted(
        Material src,
        string srcPath,
        Material template,
        ConversionMap map,
        Dictionary<string, ConversionEntry> bySource,
        HashSet<string> ownedGuids,
        ref int created,
        ref int reused)
    {
        string srcGuid = AssetDatabase.AssetPathToGUID(srcPath);

        if (bySource.TryGetValue(srcGuid, out ConversionEntry existing))
        {
            Material cached = LoadByGuid(existing.converted);

            if (cached != null)
            {
                reused++;
                return cached;
            }

            // 기록은 있는데 사본이 로드되지 않는다. **여기서 기록을 버리고 새로 만들면 원장이 조용히
            // 훼손된다.** 사본은 중첩 저장소(`Assets/Imported`)에 있고 원장은 이 저장소에 있어서,
            // Imported가 낡은 사람이 Convert를 한 번 돌리면 그 항목이 원장에서 사라지고 중복 사본이
            // 생긴다. 그 삭제가 커밋되면 원본→사본 매핑이 팀 전체에서 끊기고 `Revert`도 함께 죽는다.
            // 실측(2026-08-18): `Palette256`이 이 경로로 사본 5개까지 늘고, 원장 미등재 사본 12개가
            // 남았다 — 그중 5개는 프리팹이 참조 중이어서 이미 되돌릴 수 없다(WL-194).
            //
            // "정말 지워졌다"와 "아직 안 받았다"를 여기서 구별할 방법이 없으므로 **어느 쪽이든 멈춘다.**
            // 슬롯은 손대지 않은 채 호출자의 `unresolved`로 넘어가므로 프리팹 참조는 그대로 남는다.
            // 사본을 의도적으로 지웠다면 메뉴 「4. 원장 점검」으로 확인한 뒤 「5. 끊긴 기록 정리」로 푼다 —
            // 같은 삭제를 **사람이 Imported 최신을 확인하고 누르는 자리**로 옮긴 것이 요점이다.
            Debug.LogWarning($"{k_Tag} 원장에 기록된 사본을 로드할 수 없어 새로 만들지 않고 건너뛴다: " +
                             $"\"{existing.name}\" (converted {existing.converted}). " +
                             $"{k_OutputFolder}가 동기화됐는지 먼저 확인할 것 — 메뉴 「4. 원장 점검」(WL-194).");
            return null;
        }

        var dst = new Material(template);
        CarryOverFromSource(src, dst);

        string path = AssetDatabase.GenerateUniqueAssetPath($"{k_OutputFolder}/FK_{Sanitize(src.name)}.mat");
        AssetDatabase.CreateAsset(dst, path);
        AssetDatabase.SaveAssetIfDirty(dst);

        var entry = new ConversionEntry
        {
            name = src.name,
            source = srcGuid,
            converted = AssetDatabase.AssetPathToGUID(path),
        };

        map.entries.Add(entry);
        bySource[srcGuid] = entry;
        ownedGuids.Add(entry.converted);
        created++;

        return dst;
    }

    /// <summary>
    /// 원본 머티리얼에서 "룩이 아닌 것"만 사본으로 옮긴다.
    /// 셀 셰이딩 파라미터는 템플릿 소관이라 여기서 건드리지 않는다.
    /// </summary>
    private static void CarryOverFromSource(Material src, Material dst)
    {
        if (src.HasProperty("_BaseMap") && dst.HasProperty("_BaseMap"))
        {
            dst.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
            dst.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap"));
            dst.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap"));
        }

        if (src.HasProperty("_BaseColor") && dst.HasProperty("_BaseColor"))
        {
            dst.SetColor("_BaseColor", src.GetColor("_BaseColor"));
        }

        // 노멀맵 — CandyLand 기준 13개만 갖고 있다.
        Texture bump = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;

        if (bump != null && dst.HasProperty("_BumpMap"))
        {
            dst.SetTexture("_BumpMap", bump);

            if (src.HasProperty("_BumpScale") && dst.HasProperty("_BumpScale"))
            {
                dst.SetFloat("_BumpScale", src.GetFloat("_BumpScale"));
            }

            dst.EnableKeyword("_NORMALMAP");
        }
        else
        {
            dst.DisableKeyword("_NORMALMAP");
        }

        // 이미시브 — CandyLand 기준 4개.
        if (src.IsKeywordEnabled("_EMISSION"))
        {
            if (src.HasProperty("_EmissionMap") && dst.HasProperty("_EmissionMap"))
            {
                dst.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap"));
            }

            if (src.HasProperty("_EmissionColor") && dst.HasProperty("_EmissionColor"))
            {
                dst.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
            }

            dst.EnableKeyword("_EMISSION");
        }
        else
        {
            dst.DisableKeyword("_EMISSION");
        }

        foreach (string prop in k_SurfaceStateProps)
        {
            if (src.HasProperty(prop) && dst.HasProperty(prop))
            {
                dst.SetFloat(prop, src.GetFloat(prop));
            }
        }

        foreach (string keyword in k_CarriedKeywords)
        {
            if (src.IsKeywordEnabled(keyword))
            {
                dst.EnableKeyword(keyword);
            }
            else
            {
                dst.DisableKeyword(keyword);
            }
        }

        dst.renderQueue = src.renderQueue;
        dst.doubleSidedGI = src.doubleSidedGI;
    }

    private static IEnumerable<Renderer> CollectRenderers(IEnumerable<GameObject> roots)
    {
        if (roots == null)
        {
            yield break;
        }

        var seen = new HashSet<Renderer>();

        foreach (GameObject root in roots)
        {
            if (root == null)
            {
                continue;
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                // 파티클·라인 등은 대상이 아니다. 메시 계열만 이관한다.
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                {
                    continue;
                }

                if (seen.Add(renderer))
                {
                    yield return renderer;
                }
            }
        }
    }

    private static bool TryLoadTemplate(out Material template, out string error)
    {
        template = AssetDatabase.LoadAssetAtPath<Material>(k_TemplatePath);

        if (template == null)
        {
            error = $"{k_Tag} 템플릿 머티리얼이 없다: {k_TemplatePath}";
            return false;
        }

        if (template.shader == null || template.shader.name != k_ShaderName)
        {
            error = $"{k_Tag} 템플릿 셰이더가 '{k_ShaderName}'가 아니다: {template.shader?.name ?? "null"}";
            return false;
        }

        error = null;
        return true;
    }

    private static Material LoadByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return null;
        }

        string path = AssetDatabase.GUIDToAssetPath(guid);
        return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    private static ConversionMap LoadMap()
    {
        if (!System.IO.File.Exists(k_MapPath))
        {
            return new ConversionMap();
        }

        string json = System.IO.File.ReadAllText(k_MapPath);
        ConversionMap map = JsonUtility.FromJson<ConversionMap>(json);

        return map ?? new ConversionMap();
    }

    private static void SaveMap(ConversionMap map)
    {
        // 이름 순으로 정렬해 둔다 — 변환 순서에 따라 diff가 흔들리지 않게.
        map.entries = map.entries.OrderBy(e => e.name, StringComparer.Ordinal).ToList();

        EnsureFolder(System.IO.Path.GetDirectoryName(k_MapPath).Replace('\\', '/'));
        System.IO.File.WriteAllText(k_MapPath, JsonUtility.ToJson(map, true));
        AssetDatabase.ImportAsset(k_MapPath);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string[] parts = folder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";

            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static string Sanitize(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);

        foreach (char c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
        }

        return sb.ToString();
    }

    [Serializable]
    private class ConversionEntry
    {
        public string name;
        public string source;
        public string converted;
    }

    [Serializable]
    private class ConversionMap
    {
        public string template;
        public List<ConversionEntry> entries = new List<ConversionEntry>();
    }
}
