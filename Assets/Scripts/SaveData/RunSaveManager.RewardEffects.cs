using System;
using System.Collections.Generic;
using UnityEngine;

namespace NorthLand.Core
{
    public sealed partial class RunSaveManager
    {
        /// <summary>
        /// 모든 보상 특수효과의 현재 중첩 레벨을 수집한다.
        /// 레벨이 0인 효과도 전체 스키마에 포함한다.
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
                    Debug.LogError($"[Load] 보상 효과 컴포넌트가 없습니다: {savedEffect.Type}",this);

                    return false;
                }
            }

            // v1은 모든 효과 종류를 담는 완전한 스키마이다.
            foreach (WaveRewardType type in Enum.GetValues(typeof(WaveRewardType)))
            {
                if (!restoredTypes.Contains(type))
                {
                    Debug.LogError($"[Load] 저장 데이터에 보상 효과가 누락됐습니다: {type}",this);

                    return false;
                }
            }

            // 전체 검증이 끝난 뒤 실제 레벨을 적용한다.
            for (int i = 0; i < data.Count; i++)
            {
                RewardEffectSaveData savedEffect = data[i];

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

