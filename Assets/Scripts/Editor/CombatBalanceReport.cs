using System.Linq;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using NorthLand.Combat;

public static class CombatBalanceReport
{
    [MenuItem("Tools/NorthLand/Combat Balance Report")]
    private static void Print()
    {
        MonsterSpawn spawner =
            Object.FindFirstObjectByType<MonsterSpawn>();

        if (spawner == null)
        {
            Debug.LogError(
                "[밸런스] 씬에서 MonsterSpawn을 찾을 수 없습니다.");
            return;
        }

        SerializedProperty hpScales = new SerializedObject(spawner)
            .FindProperty("waveHpScales");

        string[] paths = AssetDatabase
            .FindAssets("t:MonsterWaveAsset", new[]
            {
                "Assets/Resources/ScriptableObjects/Wave"
            })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => System.IO.Path
                .GetFileName(path)
                .StartsWith("MonsterWave "))
            .OrderBy(WaveNumber)
            .ToArray();

        foreach (string path in paths)
        {
            MonsterWaveAsset wave =
                AssetDatabase.LoadAssetAtPath<MonsterWaveAsset>(path);

            int number = WaveNumber(path);
            int monsterCount = 0;
            float baseHp = 0f;
            float scaledHp = 0f;
            float hpScale = GetHpScale(hpScales, number);

            foreach (MonsterWaveGroup group in wave.Groups)
            {
                if (group?.MonsterPrefab == null)
                    continue;

                Enemy enemy =
                    group.MonsterPrefab.GetComponent<Enemy>();

                EnemyAsset asset =
                    enemy != null ? enemy.Asset : null;

                if (asset == null)
                    continue;

                int count = group.Count;
                float groupHp = GetMaxHp(asset) * count;

                monsterCount += count;
                baseHp += groupHp;

                scaledHp += asset.EnemyType == EnemyType.Boss
                    ? groupHp
                    : groupHp * hpScale;
            }

            float spawnTime =
                EstimateSpawnTime(wave, monsterCount);

            float incomingHpPerSecond =
                scaledHp / Mathf.Max(0.1f, spawnTime);

            Debug.Log(
                $"[밸런스] W{number:00} | " +
                $"몬스터 {monsterCount} | " +
                $"기본 HP {baseHp:0} | " +
                $"배율 x{hpScale:0.00} | " +
                $"실제 HP {scaledHp:0} | " +
                $"예상 스폰 {spawnTime:0.0}초 | " +
                $"유입 HP/s {incomingHpPerSecond:0.0}");
        }
    }

    private static float EstimateSpawnTime(
        MonsterWaveAsset wave,
        int monsterCount)
    {
        if (monsterCount <= 1)
            return 0.1f;

        float averageBatchSize =
            (1f + wave.SpawnCountPerBatch) / 2f;

        float batchCount =
            Mathf.Ceil(monsterCount / averageBatchSize);

        float averageInterval =
            (wave.MinSpawnInterval +
             wave.MaxSpawnInterval) / 2f;

        return Mathf.Max(
            0.1f,
            (batchCount - 1f) * averageInterval +
            (monsterCount - batchCount) *
            wave.IntraBatchJitter);
    }

    private static float GetHpScale(
        SerializedProperty scales,
        int waveNumber)
    {
        if (scales == null || scales.arraySize == 0)
            return 1f;

        int index = Mathf.Clamp(
            waveNumber - 1,
            0,
            scales.arraySize - 1);

        return scales
            .GetArrayElementAtIndex(index)
            .floatValue;
    }

    private static int WaveNumber(string path)
    {
        string name =
            System.IO.Path.GetFileNameWithoutExtension(path);

        string number =
            name.Replace("MonsterWave ", "");

        return int.TryParse(number, out int result)
            ? result
            : int.MaxValue;
    }

    private static float GetMaxHp(EnemyAsset asset)
    {
        return asset.EnemyType switch
        {
            EnemyType.Melee =>
                asset.Melee?.Stat?.MaxHp ?? 0f,

            EnemyType.Ranged =>
                asset.Ranged?.Stat?.MaxHp ?? 0f,

            EnemyType.Boss =>
                asset.Boss?.Stat?.MaxHp ?? 0f,

            _ => 0f
        };
    }
    [MenuItem("Tools/NorthLand/Tower Balance Report")]
    private static void PrintTowers()
    {
        EnemyAsset referenceEnemy =
            AssetDatabase.LoadAssetAtPath<EnemyAsset>(
                "Assets/Resources/ScriptableObjects/Enemies/yellow_grummy.asset");

        if (referenceEnemy == null)
        {
            Debug.LogError(
                "[밸런스] 기준 몬스터 yellow_grummy를 찾을 수 없습니다.");
            return;
        }

        float referenceHp = GetMaxHp(referenceEnemy);
        float referenceSpeed = GetMoveSpeed(referenceEnemy);

        string[] paths = AssetDatabase.FindAssets(
            "t:TowerAsset",
            new[]
            {
                "Assets/Resources/ScriptableObjects/Towers"
            });

        TowerAsset[] towers = paths
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<TowerAsset>)
            .Where(tower => tower != null)
            .OrderBy(tower => tower.TowerID)
            .ToArray();

        foreach (TowerAsset tower in towers)
        {
            float damage = tower.Attack?.AttackDamage ?? 0f;
            float interval = tower.Attack?.AttackInterval ?? 0f;
            float range = tower.Attack?.AttackRange ?? 0f;

            int pellets = Mathf.Max(
                1,
                tower.Attack?.PelletCount ?? 1);

            float dps = interval > 0f
                ? damage * pellets / interval
                : 0f;

            float rangeInTiles = range / 6f;
            float rangeStayTime = referenceSpeed > 0f
                ? range / referenceSpeed
                : 0f;
            float passDamage = dps * rangeStayTime;
            float passKills = referenceHp > 0f
                ? passDamage / referenceHp
                : 0f;

            Debug.Log(
                $"[타워] {tower.TowerID} | " +
                $"등급 {tower.Rarity} | " +
                $"해금 W{tower.UnlockWave} | " +
                $"피해 {damage:0.#} x{pellets} | " +
                $"간격 {interval:0.##}초 | " +
                $"기본 DPS {dps:0.0} | " +
                $"사거리 {rangeInTiles:0.00}타일 | " +
                $"체류 {rangeStayTime:0.00}초 | " +
                $"통과 피해 {passDamage:0.0} | " +
                $"통과 킬 {passKills:0.00} | " +
                $"명중 {tower.Impact} | " +
                $"비용 {FormatCost(tower)}");
        }
    }

    [MenuItem("Tools/NorthLand/Economy Balance Report")]
    private static void PrintEconomy()
    {
        EnemyAsset referenceEnemy =
            AssetDatabase.LoadAssetAtPath<EnemyAsset>(
                "Assets/Resources/ScriptableObjects/Enemies/yellow_grummy.asset");

        if (referenceEnemy == null)
        {
            Debug.LogError(
                "[밸런스] 기준 몬스터 yellow_grummy를 찾을 수 없습니다.");
            return;
        }

        TowerAsset[] towers = AssetDatabase
            .FindAssets("t:TowerAsset", new[]
            {
                "Assets/Resources/ScriptableObjects/Towers"
            })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<TowerAsset>)
            .Where(tower =>
                tower != null &&
                tower.Cost != null &&
                tower.Cost.Count > 0)
            .OrderBy(tower => tower.TowerID)
            .ToArray();

        for (int wave = 1; wave <= 15; wave++)
        {
            int clearedWaves = wave - 1;
            int wood = 50 + clearedWaves * 10;
            int iron = 30 + clearedWaves * 10;
            int food = 20;
            int mana = clearedWaves * 10;

            Debug.Log(
                $"[경제] W{wave:00} | " +
                $"Wood {wood} | Iron {iron} | " +
                $"Food {food} | Mana {mana}");

            foreach (TowerAsset tower in towers)
            {
                if (tower.UnlockWave > wave)
                    continue;

                int count = MaxAffordable(
                    tower, wood, iron, food, mana);

                float totalPassKills =
                    count * GetPassKills(tower, referenceEnemy);

                Debug.Log(
                    $"[경제] W{wave:00} | {tower.TowerID} | " +
                    $"최대 {count}개 | " +
                    $"총 통과 킬 {totalPassKills:0.00}");
            }
        }
    }

    private static int MaxAffordable(
        TowerAsset tower,
        int wood,
        int iron,
        int food,
        int mana)
    {
        int result = int.MaxValue;

        foreach (ResourceCost cost in tower.Cost)
        {
            if (cost?.Resource == null || cost.Amount <= 0)
                continue;

            int available = cost.Resource.ResourceID switch
            {
                "wood" => wood,
                "iron" => iron,
                "food" => food,
                "mana" => mana,
                _ => 0
            };

            result = Mathf.Min(result, available / cost.Amount);
        }

        return result == int.MaxValue ? 0 : result;
    }

    private static float GetPassKills(
        TowerAsset tower,
        EnemyAsset enemy)
    {
        float interval = tower.Attack?.AttackInterval ?? 0f;
        float hp = GetMaxHp(enemy);
        float speed = GetMoveSpeed(enemy);

        if (interval <= 0f || hp <= 0f || speed <= 0f)
            return 0f;

        float damage = tower.Attack.AttackDamage;
        int pellets = Mathf.Max(1, tower.Attack.PelletCount);
        float dps = damage * pellets / interval;
        float stayTime = tower.Attack.AttackRange / speed;

        return dps * stayTime / hp;
    }

    [MenuItem("Tools/NorthLand/Balance Summary Report")]
    private static void PrintBalanceSummary()
    {
        BuildBalanceSummary(false);
    }

    [MenuItem("Tools/NorthLand/Export Balance CSV")]
    private static void ExportBalanceCsv()
    {
        BuildBalanceSummary(true);
    }

    private static void BuildBalanceSummary(bool exportCsv)
    {
        MonsterSpawn spawner =
            Object.FindFirstObjectByType<MonsterSpawn>();
        EnemyAsset referenceEnemy =
            AssetDatabase.LoadAssetAtPath<EnemyAsset>(
                "Assets/Resources/ScriptableObjects/Enemies/yellow_grummy.asset");

        if (spawner == null || referenceEnemy == null)
        {
            Debug.LogError(
                "[밸런스] MonsterSpawn 또는 yellow_grummy를 찾을 수 없습니다.");
            return;
        }

        SerializedProperty hpScales = new SerializedObject(spawner)
            .FindProperty("waveHpScales");
        float referenceHp = GetMaxHp(referenceEnemy);

        TowerAsset[] towers = AssetDatabase
            .FindAssets("t:TowerAsset", new[]
            {
                "Assets/Resources/ScriptableObjects/Towers"
            })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<TowerAsset>)
            .Where(tower =>
                tower != null &&
                tower.Cost != null &&
                tower.Cost.Count > 0 &&
                GetPassKills(tower, referenceEnemy) > 0f)
            .OrderBy(tower => tower.TowerID)
            .ToArray();

        string[] wavePaths = AssetDatabase
            .FindAssets("t:MonsterWaveAsset", new[]
            {
                "Assets/Resources/ScriptableObjects/Wave"
            })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => System.IO.Path
                .GetFileName(path)
                .StartsWith("MonsterWave "))
            .OrderBy(WaveNumber)
            .ToArray();

        StringBuilder csv = exportCsv
            ? new StringBuilder(
                "Wave,Tower,Wood,Iron,Food,Mana,RequiredKills," +
                "AffordableCount,AvailableKills,LoadPercent,Rating\n")
            : null;

        foreach (string path in wavePaths)
        {
            MonsterWaveAsset wave =
                AssetDatabase.LoadAssetAtPath<MonsterWaveAsset>(path);
            int number = WaveNumber(path);
            float hpScale = GetHpScale(hpScales, number);
            float scaledHp = 0f;

            foreach (MonsterWaveGroup group in wave.Groups)
            {
                if (group?.MonsterPrefab == null)
                    continue;

                Enemy enemy =
                    group.MonsterPrefab.GetComponent<Enemy>();
                EnemyAsset asset = enemy != null ? enemy.Asset : null;

                if (asset == null)
                    continue;

                float groupHp = GetMaxHp(asset) * group.Count;
                scaledHp += asset.EnemyType == EnemyType.Boss
                    ? groupHp
                    : groupHp * hpScale;
            }

            float requiredKills = referenceHp > 0f
                ? scaledHp / referenceHp
                : 0f;
            int clearedWaves = number - 1;
            int wood = 50 + clearedWaves * 10;
            int iron = 30 + clearedWaves * 10;
            int food = 20;
            int mana = clearedWaves * 10;

            foreach (TowerAsset tower in towers)
            {
                if (tower.UnlockWave > number)
                    continue;

                int count = MaxAffordable(
                    tower, wood, iron, food, mana);
                float capacity =
                    count * GetPassKills(tower, referenceEnemy);
                float load = capacity > 0f
                    ? requiredKills / capacity * 100f
                    : float.PositiveInfinity;
                string rating = ClassifyLoad(load);

                Debug.Log(
                    $"[종합] W{number:00} | {tower.TowerID} | " +
                    $"요구 {requiredKills:0.00}킬 | " +
                    $"가능 {capacity:0.00}킬 | " +
                    $"사용률 {(float.IsInfinity(load) ? "∞" : load.ToString("0.0"))}% | " +
                    $"{rating}");

                csv?.AppendLine(
                    $"{number},{tower.TowerID},{wood},{iron},{food},{mana}," +
                    $"{requiredKills:0.00},{count},{capacity:0.00}," +
                    $"{(float.IsInfinity(load) ? "Infinity" : load.ToString("0.0"))},{rating}");
            }
        }

        if (csv == null)
            return;

        string directory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../BalanceReports"));
        string filePath = Path.Combine(
            directory, "NorthLand_Balance_Summary.csv");

        Directory.CreateDirectory(directory);
        File.WriteAllText(
            filePath,
            csv.ToString(),
            new UTF8Encoding(true));

        Debug.Log($"[밸런스] CSV 저장 완료: {filePath}");
    }

    private static string ClassifyLoad(float load)
    {
        if (load > 100f) return "화력 부족";
        if (load > 85f) return "어려움";
        if (load >= 70f) return "적정";
        return "쉬움";
    }

    private static string FormatCost(TowerAsset tower)
    {
        if (tower.Cost == null || tower.Cost.Count == 0)
            return "합성 전용";

        return string.Join(
            ", ",
            tower.Cost
                .Where(cost =>
                    cost != null &&
                    cost.Resource != null)
                .Select(cost =>
                    $"{cost.Resource.ResourceID} {cost.Amount}"));
    }

    private static float GetMoveSpeed(EnemyAsset asset)
    {
        return asset.EnemyType switch
        {
            EnemyType.Melee =>
                asset.Melee?.Stat?.MoveSpeed ?? 0f,

            EnemyType.Ranged =>
                asset.Ranged?.Stat?.MoveSpeed ?? 0f,

            EnemyType.Boss =>
                asset.Boss?.Stat?.MoveSpeed ?? 0f,

            _ => 0f
        };
    }
}
