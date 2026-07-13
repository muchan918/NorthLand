using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class MonsterSpawn : MonoBehaviour
{
    [Header("Common References")]
    [SerializeField] private Transform fallbackSpawnPoint;
    [SerializeField] private Transform monsterParent;

    private bool hasGeneratedSpawnPoint;
    private Vector3 generatedSpawnPosition;
    private Quaternion generatedSpawnRotation = Quaternion.identity;
    protected CancellationTokenSource SpawnCancellationTokenSource { get; private set; }

    protected Transform MonsterParent => monsterParent;

    protected virtual void OnDisable()
    {
        CancelSpawnTasks();
    }

    protected virtual void OnDestroy()
    {
        CancelSpawnTasks();
    }

    public void SetSpawnPoint(Vector3 position, Quaternion rotation)
    {
        generatedSpawnPosition = position;
        generatedSpawnRotation = rotation;
        hasGeneratedSpawnPoint = true;
    }

    public virtual void StartRound(int round)
    {
        Debug.LogWarning($"{GetType().Name}: StartRound is not implemented.", this);
    }

    protected CancellationToken RestartSpawnTasks()
    {
        CancelSpawnTasks();

        SpawnCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy()
        );

        return SpawnCancellationTokenSource.Token;
    }

    protected bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
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

    protected void SpawnPrefab(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"{GetType().Name}: monster prefab is missing.", this);
            return;
        }

        if (!TryGetSpawnPose(out Vector3 position, out Quaternion rotation))
        {
            Debug.LogWarning($"{GetType().Name}: spawn point is missing.", this);
            return;
        }

        Instantiate(prefab, position, rotation, monsterParent);
    }

    protected void CancelSpawnTasks()
    {
        if (SpawnCancellationTokenSource == null)
        {
            return;
        }

        SpawnCancellationTokenSource.Cancel();
        SpawnCancellationTokenSource.Dispose();
        SpawnCancellationTokenSource = null;
    }
}
