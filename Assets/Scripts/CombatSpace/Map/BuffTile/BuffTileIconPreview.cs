using System.Collections.Generic;
using UnityEngine;

namespace CombatSpace
{
    public sealed class BuffTileIconPreview : MonoBehaviour
    {
        private readonly List<CombatMapTileView> buffTileViews = new();

        public void Register(CombatMapTileView tileView)
        {
            if (tileView == null ||tileView.BuffDefinition == null ||tileView.BuffDefinition.Icon == null ||buffTileViews.Contains(tileView))
            {
                return;
            }

            buffTileViews.Add(tileView);
            tileView.SetBuffIconVisible(false);
        }

        public void Unregister(CombatMapTileView tileView)
        {
            if (tileView == null)
            {
                return;
            }

            buffTileViews.Remove(tileView);
        }

        public void ShowAll()
        {
            RemoveDestroyedTiles();

            foreach (CombatMapTileView tileView in buffTileViews)
            {
                tileView.SetBuffIconVisible(true);
            }
        }

        public void HideAll()
        {
            RemoveDestroyedTiles();

            foreach (CombatMapTileView tileView in buffTileViews)
            {
                tileView.SetBuffIconVisible(false);
            }
        }

        public void Clear()
        {
            HideAll();
            buffTileViews.Clear();
        }

        private void RemoveDestroyedTiles()
        {
            buffTileViews.RemoveAll(tileView => tileView == null);
        }

        private void OnDisable()
        {
            HideAll();
        }


    }
}