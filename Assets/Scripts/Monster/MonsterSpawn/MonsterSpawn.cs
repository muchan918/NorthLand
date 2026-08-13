using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using NorthLand.Combat;
using NorthLand.Core;

public class MonsterSpawn : MonoBehaviour
{
    public event Action<int> WaveCleared;

    [Header("Common References")]
    [SerializeField] private Transform fallbackSpawnPoint;
    [SerializeField] private Transform monsterParent;

    [Header("ScriptableObject Data")]
    [SerializeField] private MonsterSpawnWaveProvider waveProvider;
    [SerializeField] private bool playOnStart;
    [SerializeField] private int startRound = 1;

    [Header("Wave HP Scale (Docs/Core/CombatBalance.md §4.7)")]
    // 웨이브 진행에 따른 몬스터 최대 HP 배율. index 0 = 웨이브 1.
    //
    // **왜 웨이브 SO가 아니라 여기 배열인가**: 곡선이 15개 에셋으로 흩어지면 "W7만 안 올렸다" 같은
    // 누락이 조용히 생기고, 전체 재산정(실제로 §4.7에서 두 번 있었다)마다 15개를 다시 열어야 한다.
    // 몬스터별로 다른 배율을 줄 이유도 없다 — 난이도 곡선은 웨이브 단위 성질이다.
    //
    // ⚠ **수식이 아니라 테이블인 이유**: 경제가 감당하는 화력이 계단형(W1~5 평평 → W6~11 급상승)이라
    // "웨이브당 +N%" 같은 균일 증가율을 걸면 W5에서 난이도가 튀고 W11에서 헐거워지는 톱니가 된다.
    // 감당 곡선에 맞춰 역산한 값이므로 손으로 조정할 때도 §4.7의 여유 배율 표를 함께 볼 것.
    //
    // ⚠ **보스는 이 배율을 타지 않는다**(§4.7 파생 ③) — 등장 웨이브를 알고 정한 절대값이므로
    //   배율을 또 곱하면 이중이 된다. 제외 판정은 SpawnPrefab의 `enemy.IsBoss` 검사가 담당한다.
    [Tooltip("웨이브별 몬스터 최대 HP 배율. index 0 = 웨이브 1. 배열보다 큰 웨이브는 마지막 값을 쓴다.")]
    [SerializeField]
    private float[] waveHpScales =
    {
        1.00f, 1.00f, 1.10f, 1.15f, 1.20f,   // W1~5  — 경제 감당이 평평한 구간(배우는 구간)
        1.60f, 2.05f, 2.40f, 3.10f, 3.85f,   // W6~10 — 주민·업그레이드 투자가 결실을 보며 급상승
        4.85f, 5.05f, 5.25f, 5.35f, 5.45f,   // W11~15
    };

    [Header("Gate (성문)")]
    // 통합 계약(팀 규칙 #8): 성문 프리팹(현재 BaseGate)은 Assets/Imported(중첩 git repo) 소재다.
    //  · 팀원은 Imported repo를 함께 동기화해야 이 참조(GUID)가 살아있다 — 안 하면 성문 미생성 → GameOver 미동작.
    //  · 프리팹은 반드시 PlayerBase 컴포넌트 + PlayerBase 레이어(9)를 가져야 몬스터 교전·본진 파괴 판정이 성립한다.
    [Tooltip("몬스터가 도달해 제거되는 경로 끝점에 생성할 성문 프리팹. 비워두면 생성하지 않는다.")]
    [SerializeField] private GameObject gatePrefab;

    [SerializeField] private Vector3 gatePositionOffset = new(0f, 0f, -3f);
    [SerializeField] private Vector3 gateRotation = new(0f, -17f, 0f);
    [SerializeField] private Vector3 gateScale = new(3f, 3f, 3f);

    private Transform gateCoordinateRoot;

    private GameObject gateInstance;
    private bool hasGeneratedSpawnPoint;
    private Vector3 generatedSpawnPosition;
    private Quaternion generatedSpawnRotation = Quaternion.identity;
    private readonly List<Vector3> route = new List<Vector3>();
    private readonly List<Vector3> spawnRoute = new List<Vector3>();
    private CancellationTokenSource spawnCancellationTokenSource;
    private int currentRound;

    // 강제 클리어로 건너뛴 라운드(0 = 없음). CombatMapMonsterConnector는 OnDayToNight에서 맵 공개를
    // 기다린 뒤에야 StartRound를 부르는데, 그 대기 중에 ForceClearWave가 들어오면 보상 UI가 await로
    // 떠 있는 동안 페이즈가 Night 그대로라 뒤늦게 도착한 StartRound가 게이트를 통과해 버린다.
    // 그 1회를 삼켜 보상 패널 위로 유령 스폰이 뜨는 것을 막는다.
    private int suppressedRound;


    private void Awake()
    {
        if (waveProvider == null)
        {
            waveProvider = GetComponent<MonsterSpawnWaveProvider>();
        }
    }

    private void Start()
    {
        if (playOnStart)
        {
            StartRound(startRound);
        }
    }

    private void OnDisable()
    {
        CancelSpawnTasks();
    }

    private void OnDestroy()
    {
        CancelSpawnTasks();
    }

    /// <summary>
    /// 성문 위치 오프셋과 회전을 계산할 좌표 기준을 지정한다.
    /// </summary>
    public void SetGateCoordinateRoot(Transform coordinateRoot)
    {
        gateCoordinateRoot = coordinateRoot;

        // 경로가 먼저 설정된 경우에도 즉시 성문 Transform을 다시 적용한다.
        UpdateGate();
    }

    public void SetSpawnPoint(Vector3 position, Quaternion rotation)
    {
        generatedSpawnPosition = position;
        generatedSpawnRotation = rotation;
        hasGeneratedSpawnPoint = true;
    }

    public void SetRoute(IReadOnlyList<Vector3> routePoints)
    {
        route.Clear();

        if (routePoints == null)
        {
            return;
        }

        route.AddRange(routePoints);

        UpdateGate();
    }

    // 성문(gatePrefab)을 경로 끝점 — 몬스터가 도달해 제거되는 지점 — 에 배치한다.
    // 몬스터는 GetSpawnRoute()(route를 뒤집은 경로)를 따라가며,
    // 경로 완료 시 Enemy가 IRouteMovementAgent.RouteCompleted를 받아 제거한다.
    // 그 지점은 route[0]에 해당한다. 경로가 갱신되면(스테이지 재생성 등) 위치를 옮긴다.
    // monsterParent에 붙이지 않는다 — 웨이브 클리어는 monsterParent.childCount로 판정하므로(WL-037).
    private void UpdateGate()
    {
        if (gatePrefab == null || route.Count == 0)
        {
            return;
        }

        Vector3 endPoint = route[0];

        if (gateInstance == null)
        {
            gateInstance = Instantiate(gatePrefab);
        }

        Vector3 worldOffset = gateCoordinateRoot != null? gateCoordinateRoot.TransformVector(gatePositionOffset): gatePositionOffset;

        Quaternion worldRotation = gateCoordinateRoot != null? gateCoordinateRoot.rotation * Quaternion.Euler(gateRotation): Quaternion.Euler(gateRotation);

        gateInstance.transform.position = endPoint + worldOffset;
        gateInstance.transform.rotation = worldRotation;
        gateInstance.transform.localScale = gateScale;
    }

    public void StartRound(int round)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        // 이미 강제 클리어된 라운드의 지연 스폰 요청 — 1회만 삼키고 플래그를 소비한다.
        if (round == suppressedRound)
        {
            suppressedRound = 0;

            Debug.Log($"[몬스터 스포너] 웨이브 {round}는 강제 클리어됨 - 지연 스폰 요청을 무시합니다.");

            return;
        }

        // 승패가 확정된 뒤(승리/게임오버)에는 어떤 경로로도 새 웨이브를 시작하지 않는다.
        // 임시 치트 패널로 페이즈를 강제 전환해도 유령 스폰이 생기지 않게 하는 방어선.
        if (GameManager.Instance != null &&
            GameManager.Instance.Result != GameResult.Playing)
        {
            return;
        }

        if (DayNightManager.Instance != null &&
            DayNightManager.Instance.CurrentPhase == DayNightManager.Phase.Day)
        {
            Debug.LogWarning($"[몬스터 스포너] 낮에는 몬스터를 소환하지 않습니다. 라운드 {round} 소환을 건너뜁니다.");
            return;
        }

        // 스폰할 웨이브가 없으면(스포너 미설정 또는 이 라운드 데이터 없음) 그 밤이 EndNight에
        // 닿지 못해 소프트락된다 → "즉시 클리어"로 간주해 밤을 끝낸다(WL-037).
        if (waveProvider == null)
        {
            Debug.LogWarning("[몬스터 스포너] waveProvider 미설정 — 스폰 없이 즉시 웨이브 클리어(밤 종료) 처리합니다.");
            EndNightIfNight();
            return;
        }
        if (!waveProvider.TryGetWave(round,out IReadOnlyList<MonsterSpawnEntry> entries))
        {
            Debug.LogWarning($"Wave {round} 데이터가 없습니다.",this);

            EndNightIfNight();
            return;
        }

        currentRound = round;

        CancellationToken cancellationToken =
            RestartSpawnTasks();

        SpawnRoundAsync(round,entries,cancellationToken).Forget();
    }

    // [테스트 훅] 남은 웨이브를 즉시 클리어 처리한다. 대기 중 스폰을 멈추고(진행 중 SpawnRoundAsync는
    // 취소로 조용히 종료 → WaveCleared 중복 발화 없음), 현재 스폰된 몬스터를 전부 제거한 뒤,
    // SpawnRoundAsync가 하던 완료 경로(WaveCleared→보상→EndNight)를 직접 구동한다.
    // childCount==0 자연 충족을 기다리지 않는 이유: 그 WaitUntil이 SpawnRoundAsync 내부에 있어
    // 스폰 취소 시 도달하지 못하기 때문(스폰 도중엔 아직 시작조차 안 함).
    public void ForceClearWave()
    {
        // 통보할 웨이브 번호의 정본은 DayNightManager다. currentRound는 StartRound가 검증을 전부
        // 통과한 뒤에만 대입되므로, 몬스터 스폰 전에 클리어하면 0이거나 직전 웨이브 값(낡음)으로 남아
        // 보상 풀 조회(MonsterSpawnWaveProvider.TryGetRewardPool)가 어긋난다.
        // 밤 진행 중에는 두 값이 항상 일치한다 — CombatMapMonsterConnector가 CurrentWave를 그대로
        // StartRound에 넘기기 때문.
        DayNightManager dayNight = DayNightManager.Instance;
        int wave = dayNight != null ? dayNight.CurrentWave : currentRound;

        // 이 밤의 StartRound가 아직 도착하지 않았을 때만 억제한다.
        // 이미 스폰 중이었다면 지연 호출 자체가 없으므로 억제할 대상도 없다.
        if (currentRound != wave)
        {
            suppressedRound = wave;
        }

        CancelSpawnTasks();
        ClearSpawnedMonsters();

        if (WaveCleared != null)
        {
            WaveCleared.Invoke(wave);
        }
        else
        {
            EndNightIfNight();
        }
    }

    // monsterParent의 자식(=살아있는 몬스터)을 역순으로 제거한다.
    private void ClearSpawnedMonsters()
    {
        if (monsterParent == null)
        {
            return;
        }

        for (int i = monsterParent.childCount - 1; i >= 0; i--)
        {
            Destroy(monsterParent.GetChild(i).gameObject);
        }
    }


    private async UniTaskVoid SpawnRoundAsync(int round,IReadOnlyList<MonsterSpawnEntry> entries,CancellationToken cancellationToken)
    {
        try
        {
            List<UniTask> groupTasks = new List<UniTask>();
            float elapsedDelay = 0f;

            foreach (MonsterSpawnEntry entry in entries.OrderBy(e => e.StartDelay))
            {
                cancellationToken.ThrowIfCancellationRequested();

                float waitTime = Mathf.Max(0f, entry.StartDelay - elapsedDelay);
                if (waitTime > 0f)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(waitTime),
                        cancellationToken: cancellationToken
                    );

                    elapsedDelay = entry.StartDelay;
                }

                groupTasks.Add(SpawnGroupAsync(entry, cancellationToken));
            }

            await UniTask.WhenAll(groupTasks);

            // 스폰이 모두 끝난 뒤, 살아있는 몬스터(monsterParent의 자식)가 0이 되면 웨이브 클리어.
            // WL-037 주의: 몬스터가 성문 사거리에서 IsStopped로 멈춰 공격하도록 바뀌면서(Enemy가 이동 구동),
            // 기존 "경로 끝 도달→디스폰" 경로가 성문 교전으로 대체됐다. 따라서 childCount==0은 이제
            // '전원 처치' 또는 '성문 파괴(=GameOver)'로만 도달한다 — 타워 사거리 밖에서 성문을 두들기는
            // 몬스터가 남으면 '웨이브 성공' 경로로는 종료 불가(GameOver로만 수렴). 밤 종료 판정을
            // childCount에서 분리하는 것은 WL-037 후속 과제.
            if (monsterParent == null)
            {
                Debug.LogWarning("[몬스터 스포너] monsterParent 미할당 — 웨이브 클리어 자동 감지를 건너뜁니다.");
                return;
            }

            await UniTask.WaitUntil(() => monsterParent.childCount == 0,cancellationToken: cancellationToken);

            if (WaveCleared != null)
            {
                WaveCleared.Invoke(round);
            }
            else
            {
                Debug.LogWarning("WaveCleared 구독자가 없어 기존 방식으로 밤을 종료합니다.",this);

                EndNightIfNight();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // 웨이브 클리어 시 밤을 종료한다. 밤이 아닐 때(수동으로 이미 낮 전환 등) 호출은 무시한다.
    // DayNightManager는 StartRound에서 이미 참조하는 의존이라 새 결합을 늘리지 않는다.
    private void EndNightIfNight()
    {
        DayNightManager dayNight = DayNightManager.Instance;
        if (dayNight == null || dayNight.CurrentPhase != DayNightManager.Phase.Night)
        {
            return;
        }

        dayNight.EndNight();
    }

    private async UniTask SpawnGroupAsync(MonsterSpawnEntry entry, CancellationToken cancellationToken)
    {
        int spawnCount = Mathf.Max(0, entry.Count);

        for (int i = 0; i < spawnCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpawnPrefab(entry.MonsterPrefab);

            if (i < spawnCount - 1 && entry.SpawnInterval > 0f)
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(entry.SpawnInterval),
                    cancellationToken: cancellationToken
                );
            }
        }
    }

    private CancellationToken RestartSpawnTasks()
    {
        CancelSpawnTasks();

        spawnCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy()
        );

        return spawnCancellationTokenSource.Token;
    }

    private bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        if (hasGeneratedSpawnPoint)
        {
            position = generatedSpawnPosition;
            rotation = generatedSpawnRotation;
            return true;
        }

        if (fallbackSpawnPoint != null)
        {
            position = fallbackSpawnPoint.position;
            rotation = fallbackSpawnPoint.rotation;
            return true;
        }

        position = default;
        rotation = Quaternion.identity;
        return false;
    }

    /// 해당 웨이브의 몬스터 최대 HP 배율(§4.7). 표 밖의 웨이브는 **마지막 값으로 고정**한다 —
    /// 0이나 1로 떨어뜨리면 15웨이브를 넘겨 플레이했을 때 난이도가 갑자기 무너진다.
    /// 배열이 비어 있으면 무보정(1)이라 배선을 잊어도 게임이 성립한다.
    private float WaveHpScale(int wave)
    {
        if (waveHpScales == null || waveHpScales.Length == 0)
        {
            return 1f;
        }

        int index = Mathf.Clamp(wave - 1, 0, waveHpScales.Length - 1);
        float scale = waveHpScales[index];

        // 0 이하는 저작 실수다(빈 칸으로 남긴 슬롯 등). 무보정으로 폴백하되 조용히 넘기지 않는다 —
        // HP가 0이 되면 몬스터가 스폰 즉시 죽어 "웨이브가 그냥 지나간다"로만 보인다.
        if (scale <= 0f)
        {
            Debug.LogError($"[몬스터 스포너] 웨이브 {wave}의 HP 배율이 {scale}입니다 — 무보정(1)으로 처리합니다. " +
                           "waveHpScales 배열을 확인하세요.", this);
            return 1f;
        }

        return scale;
    }

    // ── 런타임 소환 창구(#233) ─────────────────────────────
    // 보스 BT의 지속 소환 패턴이 EnemyAgent를 경유해 호출한다. 웨이브 스폰과 같은 경로를 쓰므로
    // 소환체도 monsterParent 자식으로 들어가고 경로를 받는다 — 웨이브 클리어 판정이
    // monsterParent.childCount == 0이라(line 230 참조) 소환체를 밖에 두면 보스 사망 즉시
    // 웨이브가 종료되면서 잡몹이 남는다. 안에 두면 "보스를 죽여야 물결이 멎는다"가 성립한다.
    //
    // 스포너를 정적 싱글톤으로 노출하지 않는다 — 스포너가 여러 개인 구성을 막지 않기 위해
    // 소환체는 자기를 만든 스포너에 스폰 시점 주입으로 묶인다(SpawnPrefab 참조).
    public GameObject SpawnMonster(GameObject prefab)
    {
        return SpawnPrefab(prefab);
    }

    // 현재 살아있는 몬스터 수(= monsterParent 자식 수). 소환 상한(MaxAlive) 판정용.
    // 주의 2건: ① 보스 자신도 포함된다 ② 사망 연출 중인 몬스터도 destroyDelay(2초) 동안
    // 포함된다(MonsterStateMachine.cs:152, WL-038). 상한값을 정할 때 이 오차를 감안해야 한다.
    public int AliveMonsterCount => monsterParent != null ? monsterParent.childCount : 0;

    private GameObject SpawnPrefab(GameObject prefab)
    {
        if (prefab == null)
        {
            return null;
        }

        if (!TryGetSpawnPose(out Vector3 position, out Quaternion rotation))
        {
            return null;
        }

        GameObject monster = Instantiate(prefab,position,rotation,monsterParent);

        Enemy enemy = monster.GetComponent<Enemy>();
        IRouteMovementAgent routeMovement = monster.GetComponentInChildren<IRouteMovementAgent>();

        // 1. 필수 컴포넌트부터 검사
        if (enemy == null || routeMovement == null)
        {
            Debug.LogError(
                $"[{monster.name}] Enemy 또는 IRouteMovementAgent가 연결되지 않았습니다.",
                monster
            );

            Destroy(monster);
            return null;
        }

        // 2. 데이터와 이동 컴포넌트의 모드가 일치하는지 검사
        if (enemy.MovementMode != routeMovement.SupportedMode)
        {
            Debug.LogError($"[{monster.name}] 이동 모드가 일치하지 않습니다. EnemyAsset: {enemy.MovementMode}, " +
                $"MovementAgent: {routeMovement.SupportedMode}",monster);

            Destroy(monster);
            return null;
        }

        // 3. 공중 이동 컴포넌트는 몬스터 루트에 있어야 함
        if (routeMovement.SupportedMode == MovementMode.Flying &&routeMovement is MonoBehaviour movementComponent &&movementComponent.transform != monster.transform)
        {
            Debug.LogError($"[{monster.name}] 공중 이동 컴포넌트는 몬스터 루트에 연결해야 합니다.",monster);

            Destroy(monster);
            return null;
        }


        // BT 소환 노드가 스포너를 거쳐야 소환체를 monsterParent에 넣을 수 있는데, 프리팹은
        // 씬 참조를 들 수 없다. 그래서 경로를 주입하는 이 자리에서 스포너 자신도 주입한다.
        // EnemyAgent가 없는 프리팹(일반 잡몹)은 그냥 건너뛴다 — 선택적 의존이다.
        EnemyAgent agent = monster.GetComponentInChildren<EnemyAgent>();

        if (agent != null)
        {
            agent.BindSpawner(this);
        }

        // 웨이브 HP 배율 주입(§4.7). **경로 설정 전에** 부른다 — 배율은 현재 HP를 새 최대치로
        // 다시 채우므로, 이동이 시작되기 전에 끝내야 HP UI가 중간값을 한 프레임 보여주지 않는다.
        //
        // ⚠ **보스는 배율을 타지 않는다**(§4.7 파생 ③). 보스 HP는 등장 웨이브를 알고 손으로 정한
        // 절대값이라 배율을 또 곱하면 이중 계산이 된다 — 최종보스(W15, 배율 ×5.45)라면 5배가 넘어간다.
        // 그리고 중간보스는 가속 패턴이 실효 내구력을 배로 만들어(§11) HP 축으로 올리면 못 잡는다.
        //
        // 보스 BT 소환체(잡몹)는 IsBoss가 아니므로 정상적으로 배율을 받는다 — 소환체는 그 웨이브의
        // 잡몹과 같은 취급이 맞다.
        //
        // ★ **웨이브 번호는 `currentRound`가 아니라 `DayNightManager`에서 읽는다**(ForceClearWave와 같은 패턴).
        //   웨이브 스폰 경로만 보면 `currentRound`로 충분하다 — `SpawnRoundAsync`가 대입 직후에 돌기 때문.
        //   하지만 이 메서드는 **공개 소환 창구 `SpawnMonster`**(보스 BT의 지속 소환)도 함께 타고, 그쪽은
        //   `StartRound` 타이밍과 무관하게 불린다. `currentRound`는 검증을 통과한 뒤에야 대입되므로
        //   그 전에는 0이거나 직전 웨이브 값(낡음)이고, `WaveHpScale(0)`은 인덱스가 클램프돼 **배율 1.0**을
        //   돌려준다 — W15(×5.45)에서 소환된 잡몹이 5배 넘게 약해진다.
        //
        // ⚠ **이 오작동은 아무 신호도 내지 않는다.** `WaveHpScale`의 LogError는 배율이 0 이하일 때만 나는데
        //   여기서 나오는 값은 정상 범위인 1.0이다. 증상이 "보스가 부르는 잡몹이 좀 약하다"뿐이라
        //   원인에서 멀다 — 그래서 값이 맞는 경로가 아니라 **읽는 출처**를 고정한다.
        if (!enemy.IsBoss)
        {
            DayNightManager dayNight = DayNightManager.Instance;
            int wave = dayNight != null ? dayNight.CurrentWave : currentRound;

            enemy.ApplyWaveHpScale(WaveHpScale(wave));
        }

        // Enemy가 IRouteMovementAgent.RouteCompleted를 구독하여
        // 경로 끝 도달 시 몬스터 루트 오브젝트를 제거한다.
        routeMovement.SetRoute(GetSpawnRoute());

        return monster;
    }

    private List<Vector3> GetSpawnRoute()
    {
        spawnRoute.Clear();

        for (int i = route.Count - 1; i >= 0; i--)
        {
            spawnRoute.Add(route[i]);
        }

        return spawnRoute;
    }

    private void CancelSpawnTasks()
    {
        if (spawnCancellationTokenSource == null)
        {
            return;
        }

        spawnCancellationTokenSource.Cancel();
        spawnCancellationTokenSource.Dispose();
        spawnCancellationTokenSource = null;
    }
}
