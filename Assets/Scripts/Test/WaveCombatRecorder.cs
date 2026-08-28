using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NorthLand.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
public sealed class WaveCombatRecorder : MonoBehaviour
{
    const string Header =
        "Wave,Status,Duration,Tower,Hits,Damage,Kills,Leaks,BaseDamage,BaseHP,Wood,Iron,Food,Mana\n";

    static WaveCombatRecorder instance;

    readonly Dictionary<string, float> damageBySource = new();
    readonly Dictionary<string, int> hitsBySource = new();
    readonly Dictionary<string, int> killsBySource = new();
    readonly HashSet<int> enemiesAtBase = new();

    MonsterSpawn spawner;
    int currentWave;
    float startedAt;
    float baseDamage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        EnsureInstance();
    }

    static void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureInstance();

    static void EnsureInstance()
    {
        if (instance == null)
        {
            var go = new GameObject(nameof(WaveCombatRecorder));
            DontDestroyOnLoad(go);
            instance = go.AddComponent<WaveCombatRecorder>();
        }

        instance.BindSpawner();
    }

    void OnEnable()
    {
        Enemy.Damaged += HandleEnemyDamaged;
        Enemy.Killed += HandleEnemyKilled;
        PlayerBase.Damaged += HandleBaseDamaged;
    }

    void OnDisable()
    {
        Enemy.Damaged -= HandleEnemyDamaged;
        Enemy.Killed -= HandleEnemyKilled;
        PlayerBase.Damaged -= HandleBaseDamaged;
        UnbindSpawner();
    }

    void BindSpawner()
    {
        MonsterSpawn found = FindFirstObjectByType<MonsterSpawn>();
        if (found == spawner) return;

        UnbindSpawner();
        spawner = found;

        if (spawner == null) return;
        spawner.WaveStarted += HandleWaveStarted;
        spawner.WaveCleared += HandleWaveCleared;
    }

    void UnbindSpawner()
    {
        if (spawner == null) return;
        spawner.WaveStarted -= HandleWaveStarted;
        spawner.WaveCleared -= HandleWaveCleared;
        spawner = null;
    }

    void HandleWaveStarted(int wave)
    {
        currentWave = wave;
        startedAt = Time.unscaledTime;
        baseDamage = 0f;
        damageBySource.Clear();
        hitsBySource.Clear();
        killsBySource.Clear();
        enemiesAtBase.Clear();
    }

    void HandleEnemyDamaged(IAttacker source, Enemy enemy, float amount)
    {
        if (currentWave == 0 || amount <= 0f) return;

        string key = SourceName(source);
        damageBySource[key] = damageBySource.GetValueOrDefault(key) + amount;
        hitsBySource[key] = hitsBySource.GetValueOrDefault(key) + 1;
    }

    void HandleEnemyKilled(IAttacker source, Enemy enemy)
    {
        if (currentWave == 0) return;

        string key = SourceName(source);
        killsBySource[key] = killsBySource.GetValueOrDefault(key) + 1;
    }

    void HandleBaseDamaged(DamageInfo info, float amount)
    {
        if (currentWave == 0 || amount <= 0f) return;

        baseDamage += amount;
        if (info.Source is Enemy enemy)
            enemiesAtBase.Add(enemy.GetInstanceID());

        if (PlayerBase.Instance != null && PlayerBase.Instance.IsDead)
            FinishWave("Failed");
    }

    void HandleWaveCleared(int wave)
    {
        if (wave == currentWave)
            FinishWave("Cleared");
    }

    void FinishWave(string status)
    {
        if (currentWave == 0) return;

        int wave = currentWave;
        float duration = Time.unscaledTime - startedAt;
        float recordedBaseDamage = baseDamage;
        int leaks = enemiesAtBase.Count;
        var damage = new Dictionary<string, float>(damageBySource);
        var hits = new Dictionary<string, int>(hitsBySource);
        var kills = new Dictionary<string, int>(killsBySource);
        currentWave = 0;

        StartCoroutine(WriteAfterFrame(
            wave, status, duration, recordedBaseDamage, leaks, damage, hits, kills));
    }

    IEnumerator WriteAfterFrame(
        int wave,
        string status,
        float duration,
        float recordedBaseDamage,
        int leaks,
        Dictionary<string, float> damage,
        Dictionary<string, int> hits,
        Dictionary<string, int> kills)
    {
        yield return null;

        ManagementController management = FindFirstObjectByType<ManagementController>();
        float baseHp = PlayerBase.Instance != null ? PlayerBase.Instance.CurrentHp : 0f;
        int wood = management != null ? management.ResourceCount(ResourceKind.Wood) : 0;
        int iron = management != null ? management.ResourceCount(ResourceKind.Iron) : 0;
        int food = management != null ? management.ResourceCount(ResourceKind.Food) : 0;
        int mana = management != null ? management.ResourceCount(ResourceKind.Mana) : 0;

        string directory = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../BalanceReports"));
        string path = Path.Combine(directory, "NorthLand_Playtest.csv");
        Directory.CreateDirectory(directory);

        if (!File.Exists(path))
            File.WriteAllText(path, Header, new UTF8Encoding(true));

        string[] sources = damage.Keys
            .Union(kills.Keys)
            .DefaultIfEmpty("none")
            .OrderBy(value => value)
            .ToArray();

        var rows = new StringBuilder();
        foreach (string source in sources)
        {
            rows.AppendLine(string.Join(",",
                wave,
                status,
                duration.ToString("0.00", CultureInfo.InvariantCulture),
                source,
                hits.GetValueOrDefault(source),
                damage.GetValueOrDefault(source).ToString("0.00", CultureInfo.InvariantCulture),
                kills.GetValueOrDefault(source),
                leaks,
                recordedBaseDamage.ToString("0.00", CultureInfo.InvariantCulture),
                baseHp.ToString("0.00", CultureInfo.InvariantCulture),
                wood,
                iron,
                food,
                mana));
        }

        File.AppendAllText(path, rows.ToString(), new UTF8Encoding(false));
        Debug.Log($"[밸런스] 실전 기록 저장: {path}");
    }

    static string SourceName(IAttacker source)
    {
        if (source is Tower tower)
            return tower.Asset != null ? tower.Asset.TowerID : tower.name;

        return source != null ? source.GetType().Name : "skill_or_environment";
    }
}
#endif