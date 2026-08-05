using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Core
{
    public sealed partial class RunSaveManager
    {
        /// <summary>
        /// 씬에 등록된 보상 특수효과의 현재 중첩 레벨을 수집한다.
        /// 등록된 효과는 레벨이 0이어도 포함하며, 컴포넌트가 없는 enum 값은 제외한다.
        /// </summary>
        private bool TryCaptureRewardEffects(out List<RewardEffectSaveData> data)
        {
            data = null;

            SkillEffectManager manager = SkillEffectManager.Instance;

            if (manager == null)
            {
                Debug.LogError("[Save] SkillEffectManager가 준비되지 않았습니다.",this);

                return false;
            }

            var captured = new List<RewardEffectSaveData>();

            foreach (WaveRewardType type in Enum.GetValues(typeof(WaveRewardType)))
            {
                if (!manager.HasEffect(type))
                    continue;

                int level = manager.GetLevel(type);

                if (level < 0)
                {
                    Debug.LogError($"[Save] 보상 효과 레벨이 음수입니다: {type}={level}",this);

                    return false;
                }

                captured.Add(new RewardEffectSaveData
                    {
                        Type = type,
                        Level = level
                    });
            }

            data = captured;
            return true;
        }

        /// <summary>
        /// 저장된 보상 특수효과 중첩 레벨을 복원한다.
        /// 전체 목록을 검증한 뒤 실제 레벨을 적용한다.
        /// </summary>
        private bool TryRestoreRewardEffects(List<RewardEffectSaveData> data)
        {
            SkillEffectManager manager = SkillEffectManager.Instance;

            if (manager == null)
            {
                Debug.LogError("[Load] SkillEffectManager가 준비되지 않았습니다.",this);

                return false;
            }

            if (data == null)
            {
                Debug.LogError("[Load] 보상 효과 세이브 데이터가 없습니다.",this);

                return false;
            }

            var restoredTypes = new HashSet<WaveRewardType>();

            // 실제 레벨을 변경하기 전에 전체 데이터를 검증한다.
            for (int i = 0; i < data.Count; i++)
            {
                RewardEffectSaveData savedEffect = data[i];

                if (savedEffect == null)
                {
                    Debug.LogError($"[Load] 보상 효과 데이터 {i}번 항목이 비어 있습니다.",this);

                    return false;
                }

                if (!Enum.IsDefined(typeof(WaveRewardType),savedEffect.Type))
                {
                    Debug.LogError($"[Load] 알 수 없는 보상 효과 종류입니다: {(int)savedEffect.Type}",this);

                    return false;
                }

                if (savedEffect.Level < 0)
                {
                    Debug.LogError($"[Load] 보상 효과 레벨이 음수입니다: {savedEffect.Type}={savedEffect.Level}",this);

                    return false;
                }

                if (!restoredTypes.Add(savedEffect.Type))
                {
                    Debug.LogError($"[Load] 중복된 보상 효과 종류가 있습니다: {savedEffect.Type}",this);

                    return false;
                }

                if (!manager.HasEffect(savedEffect.Type))
                {
                    // 미부착 효과의 0레벨은 저장 당시에도 미보유 상태였으므로 안전하게 무시한다.
                    // 1레벨 이상은 실제 획득 진행을 잃게 되므로 배선 오류로 취급한다.
                    if (savedEffect.Level == 0)
                        continue;

                    Debug.LogError(
                        $"[Load] 보유 중인 보상 효과 컴포넌트가 없습니다: " +
                        $"{savedEffect.Type}=Lv{savedEffect.Level}",
                        this);

                    return false;
                }
            }

            // 전체 검증이 끝난 뒤 실제 레벨을 적용한다.
            for (int i = 0; i < data.Count; i++)
            {
                RewardEffectSaveData savedEffect = data[i];

                if (!manager.HasEffect(savedEffect.Type))
                    continue;

                if (!manager.TryRestoreLevel(savedEffect.Type,savedEffect.Level))
                {
                    Debug.LogError($"[Load] 보상 효과 복원에 실패했습니다: {savedEffect.Type}={savedEffect.Level}",this);

                    return false;
                }
            }

            return true;
        }
    }
}

