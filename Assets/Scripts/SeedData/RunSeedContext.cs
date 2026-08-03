using System;
using System.Security.Cryptography;

namespace NorthLand.Core
{
    /// <summary>
    /// 현재 Run의 시드 데이터를 관리한다.
    /// 저장 원장은 RunData이며,
    /// 이 클래스는 생성·파생·복원·기록 API를 제공한다.
    /// </summary>
    public sealed class RunSeedContext
    {
        private RunData runData;

        public RunData RunData => runData;

        public RunSeedData Data =>
            runData != null ? runData.SeedData : null;

        public bool IsInitialized =>
            runData != null &&
            runData.SeedData != null;

        public int MasterSeed =>
            IsInitialized
                ? runData.SeedData.MasterSeed
                : 0;

        /// <summary>
        /// 무작위 마스터 시드로 새로운 Run을 시작한다.
        /// </summary>
        public RunSeedData CreateRandomRun()
        {
            int masterSeed = GenerateRandomMasterSeed();

            return CreateRun(masterSeed);
        }

        /// <summary>
        /// 지정한 마스터 시드로 새로운 Run을 시작한다.
        /// </summary>
        public RunSeedData CreateRun(int masterSeed)
        {
            RunSeedData seedData = new RunSeedData
            {
                SeedVersion =
                    RunSeedDeriver.CurrentVersion,

                MasterSeed = masterSeed,

                CombatMapRequestedSeed =
                    RunSeedDeriver.Derive(
                        masterSeed,
                        RunSeedDeriver.CombatMapTag
                    ),

                TerritoryRequestedSeed =
                    RunSeedDeriver.Derive(
                        masterSeed,
                        RunSeedDeriver.TerritoryTag
                    )
            };

            runData = new RunData
            {
                SeedData = seedData
            };

            return seedData;
        }

        /// <summary>
        /// 저장된 Run 전체 데이터를 복원한다.
        /// </summary>
        public void Restore(RunData savedRunData)
        {
            if (savedRunData == null)
            {
                throw new ArgumentNullException(
                    nameof(savedRunData)
                );
            }

            if (savedRunData.SeedData == null)
            {
                throw new InvalidOperationException(
                    "저장된 시드 데이터가 없습니다."
                );
            }

            if (savedRunData.SeedData.SeedVersion !=
                RunSeedDeriver.CurrentVersion)
            {
                throw new InvalidOperationException(
                    "지원하지 않는 시드 버전입니다. " +
                    $"저장 버전: " +
                    $"{savedRunData.SeedData.SeedVersion}, " +
                    $"현재 버전: " +
                    $"{RunSeedDeriver.CurrentVersion}"
                );
            }

            runData = savedRunData;
        }

        public void RecordCombatMapUsedSeed(int usedSeed)
        {
            EnsureInitialized();

            runData.SeedData.CombatMapUsedSeed = usedSeed;
        }

        public void RecordTerritoryUsedSeed(int usedSeed)
        {
            EnsureInitialized();

            runData.SeedData.TerritoryUsedSeed = usedSeed;
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException(
                    "Run 시드가 초기화되지 않았습니다."
                );
            }
        }

        private static int GenerateRandomMasterSeed()
        {
            byte[] bytes = new byte[4];

            using (RandomNumberGenerator generator =
                   RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            int result =
                BitConverter.ToInt32(bytes, 0) &
                int.MaxValue;

            return result == 0 ? 1 : result;
        }
    }
}