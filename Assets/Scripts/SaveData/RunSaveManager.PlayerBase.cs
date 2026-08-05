using NorthLand.Combat;
using UnityEngine;

namespace NorthLand.Core
{
    public sealed partial class RunSaveManager
    {
        /// <summary>
        /// 현재 본진 HP를 저장 데이터로 수집한다.
        /// 최대 HP는 밸런스 값이므로 저장하지 않는다.
        /// </summary>
        /// <param name="data">
        /// 수집된 본진 상태. 실패하면 null.
        /// </param>
        /// <returns>본진이 존재하고 HP가 유효하면 true.</returns>
        private bool TryCapturePlayerBase(
            out BaseSaveData data)
        {
            data = null;

            PlayerBase playerBase = PlayerBase.Instance;

            if (playerBase == null)
            {
                Debug.LogError("[Save] PlayerBase가 생성되지 않았습니다.",this);

                return false;
            }

            float currentHp = playerBase.CurrentHp;

            if (float.IsNaN(currentHp) || float.IsInfinity(currentHp))
            {
                Debug.LogError($"[Save] 본진 HP가 유효하지 않습니다: {currentHp}",playerBase);

                return false;
            }

            if (currentHp <= 0f)
            {
                Debug.LogError($"[Save] 파괴된 본진은 저장할 수 없습니다: {currentHp}",playerBase);

                return false;
            }

            if (currentHp > playerBase.MaxHp)
            {
                Debug.LogError($"[Save] 본진 HP가 최대 HP를 초과합니다: 현재={currentHp}, 최대={playerBase.MaxHp}",playerBase);

                return false;
            }

            data = new BaseSaveData
            {
                CurrentHp = currentHp
            };

            return true;
        }

        /// <summary>
        /// 저장된 현재 HP를 런타임 본진에 복원한다.
        /// 본진은 맵과 경로 생성 후 먼저 스폰되어 있어야 한다.
        /// </summary>
        /// <param name="data">복원할 본진 상태.</param>
        /// <returns>본진 HP 복원에 성공하면 true.</returns>
        private bool TryRestorePlayerBase(
            BaseSaveData data)
        {
            if (data == null)
            {
                Debug.LogError("[Load] 본진 세이브 데이터가 없습니다.",this);

                return false;
            }

            PlayerBase playerBase = PlayerBase.Instance;

            if (playerBase == null)
            {
                Debug.LogError("[Load] PlayerBase가 생성되지 않았습니다.",this);

                return false;
            }

            if (!playerBase.TryRestoreCurrentHp(data.CurrentHp))
            {
                Debug.LogError($"[Load] 본진 HP 복원에 실패했습니다: {data.CurrentHp}",this);

                return false;
            }

            return true;
        }
    }
}

