using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class NextWavePreviewView : MonoBehaviour
{
    [SerializeField] private TMP_Text waveNumberText;
    [SerializeField] private Transform content;
    [SerializeField] private NextWaveMonsterEntry entryPrefab;
    [SerializeField] private Sprite unknownMonsterIcon;

    private readonly List<NextWaveMonsterEntry> spawnedEntries = new();

    public void SetWaveNumber(int waveNumber)
    {
        if (waveNumberText != null)
        {
            waveNumberText.text = $"NEXT WAVE {waveNumber}";
        }
    }

    public void AddEntry(Sprite icon, int count)
    {
        if (content == null || entryPrefab == null)
        {
            return;
        }

        NextWaveMonsterEntry entry = Instantiate(entryPrefab, content);

        entry.Bind(icon != null ? icon : unknownMonsterIcon,count);

        spawnedEntries.Add(entry);
    }

    public void ClearEntries()
    {
        foreach (NextWaveMonsterEntry entry in spawnedEntries)
        {
            if (entry != null)
            {
                Destroy(entry.gameObject);
            }
        }

        spawnedEntries.Clear();
    }
}