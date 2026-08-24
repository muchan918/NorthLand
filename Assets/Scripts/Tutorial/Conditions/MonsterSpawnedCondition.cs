using System;
using UnityEngine;

// 몬스터가 지정한 마릿수만큼 스폰되면 충족된다.
// "적이 걸어오는 걸 잠깐 보여준 뒤 다음 안내를 띄운다" 같은 간격을 만들 때 쓴다.
//
// 시간이 아니라 스폰 수로 세는 이유는 두 가지다.
// ① 플레이어가 무엇을 하든(타워를 안 짓든, 사거리 밖에 놓든) 스폰은 반드시 일어난다 —
//    적 처치나 타워 발사를 기다리면 배치를 잘못한 플레이어가 영영 갇힌다.
// ② 스폰 간격은 게임 속도 배율을 그대로 타므로 2배속·4배속에서도 체감이 어긋나지 않는다.
//
// ⚠ 클래스 이름을 바꾸면 [SerializeReference]로 저장된 기존 스텝 데이터가 깨진다.
[Serializable]
public class MonsterSpawnedCondition : TutorialCondition
{
    [Tooltip("이 단계에 들어온 뒤 스폰되어야 하는 몬스터 수.")]
    [Min(1)]
    [SerializeField]
    private int count = 3;

    private MonsterSpawn _spawn;

    // 조건 객체는 에셋에 저장되므로 이전 플레이의 값이 남는다 — Begin에서 반드시 0으로 되돌린다.
    private int _spawned;

    public override void Begin(TutorialContext context)
    {
        _spawned = 0;
        _spawn = context.MonsterSpawn;

        if (_spawn == null)
        {
            Debug.LogWarning($"[{nameof(MonsterSpawnedCondition)}] 씬에서 MonsterSpawn을 찾지 못해 이 단계를 넘어갈 수 없다.");

            return;
        }

        _spawn.MonsterSpawned += OnMonsterSpawned;
    }

    public override void End()
    {
        if (_spawn != null)
        {
            _spawn.MonsterSpawned -= OnMonsterSpawned;
            _spawn = null;
        }
    }

    private void OnMonsterSpawned(GameObject monster)
    {
        _spawned++;

        if (_spawned >= count)
        {
            Fire();
        }
    }
}
