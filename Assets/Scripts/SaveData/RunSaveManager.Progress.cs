using System;
using UnityEngine;

namespace NorthLand.Core
{
    public sealed partial class RunSaveManager
    {
        /// <summary>
        /// 현재 웨이브 진행 상태를 수집한다.
        /// 현재 일차는 WaveCount + 1로 계산되므로 별도로 저장하지 않는다.
        /// </summary>
        private bool TryCaptureProgress(out ProgressSaveData data)
        {
            data = null;

            DayNightManager dayNight = DayNightManager.Instance;

            if (dayNight == null)
            {
                Debug.LogError("[Save] DayNightManager가 준비되지 않았습니다.",this);

                return false;
            }

            if (dayNight.WaveCount < 0)
            {
                Debug.LogError($"[Save] WaveCount가 음수입니다: {dayNight.WaveCount}",this);

                return false;
            }

            if (!Enum.IsDefined(typeof(DayNightManager.Phase),dayNight.CurrentPhase))
            {
                Debug.LogError($"[Save] 알 수 없는 페이즈입니다: {(int)dayNight.CurrentPhase}",this);

                return false;
            }

            // v1은 낮 시작 시점만 저장한다.
            if (dayNight.CurrentPhase !=DayNightManager.Phase.Day)
            {
                Debug.LogError($"[Save] v1은 낮 상태에서만 저장할 수 있습니다: {dayNight.CurrentPhase}",this);

                return false;
            }

            data = new ProgressSaveData
            {
                WaveCount = dayNight.WaveCount,
                Phase = dayNight.CurrentPhase
            };

            return true;
        }

        /// <summary>
        /// 저장된 웨이브 진행 상태를 복원한다.
        /// v1 세이브는 낮 상태만 허용한다.
        /// </summary>
        private bool TryRestoreProgress(
            ProgressSaveData data)
        {
            if (data == null)
            {
                Debug.LogError("[Load] 진행 상태 세이브 데이터가 없습니다.",this);

                return false;
            }

            DayNightManager dayNight =DayNightManager.Instance;

            if (dayNight == null)
            {
                Debug.LogError("[Load] DayNightManager가 준비되지 않았습니다.",this);

                return false;
            }

            if (data.Phase !=DayNightManager.Phase.Day)
            {
                Debug.LogError($"[Load] v1에서 지원하지 않는 페이즈입니다: {data.Phase}",this);

                return false;
            }

            if (!dayNight.TryRestoreState(data.WaveCount,data.Phase))
            {
                Debug.LogError($"[Load] 진행 상태 복원에 실패했습니다: Wave={data.WaveCount}, Phase={data.Phase}",this);

                return false;
            }

            return true;
        }
    }
}

