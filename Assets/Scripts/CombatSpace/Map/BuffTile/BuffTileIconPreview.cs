using System.Collections.Generic;
using CombatSpace;
using UnityEngine;

public sealed class BuffTileIconPreview : MonoBehaviour
{
    private readonly List<CombatMapTileView> visibleTiles = new List<CombatMapTileView>();

    public void ShowAll()
    {
        HideAll();

        CombatMapTileView[] tileViews = FindObjectsByType<CombatMapTileView>(FindObjectsSortMode.None);

        foreach (CombatMapTileView tileView in tileViews)
        {
            if (!CanShowIcon(tileView))
            {
                continue;
            }

            tileView.SetBuffIconVisible(true);
            visibleTiles.Add(tileView);
        }
    }

    public void HideAll()
    {
        foreach (CombatMapTileView tileView in visibleTiles)
        {
            if (tileView != null)
            {
                tileView.SetBuffIconVisible(false);
            }
        }

        visibleTiles.Clear();
    }

    private static bool CanShowIcon(
        CombatMapTileView tileView)
    {
        return tileView != null && tileView.BuffDefinition != null && tileView.BuffDefinition.Icon != null;
    }

    private void OnDisable()
    {
        HideAll();
    }
}