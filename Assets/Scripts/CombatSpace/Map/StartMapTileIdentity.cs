using UnityEngine;

namespace CombatSpace
{
    public sealed class StartMapTileIdentity : MonoBehaviour
    {
        [SerializeField]
        private string tileId;

        public string TileId => tileId;

        public bool HasValidId =>
            !string.IsNullOrWhiteSpace(tileId);
    }
}