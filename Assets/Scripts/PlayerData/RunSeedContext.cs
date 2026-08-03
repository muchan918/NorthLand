using System;
using System.Security.Cryptography;

namespace NorthLand.Core
{
    /// <summary>
    /// 현재 Run의 시드 데이터를 관리한다.
    /// 저장 원장은 RunSeedData이며,
    /// 이 클래스는 생성·파생·기록 API를 제공한다.
    /// </summary>
    public sealed class RunSeedContext
    {
        private RunSeedData data;

        public RunSeedData Data => data;

        public bool IsInitialized => data != null;

        public int MasterSeed => data != null ? data.MasterSeed : 0;

        /// <summary>
        /// 랜덤 마스터 시드로 새로운 Run을 시작한다.
        /// </summary>
        public RunSeedData CreateRandomRun()
        {
            int masterSeed = GenerateRandomMasterSeed();

            return CreateRun(masterSeed);
        }

        /// <summary>
        /// 플레이어가 입력한 마스터 시드로
        /// 새로운 Run을 시작한다.
        /// </summary>
        public RunSeedData CreateRun(int masterSeed)
        {
            data = new RunSeedData
            {
                SeedVersion = RunSeedDeriver.CurrentVersion,

                MasterSeed = masterSeed,

                CombatMapRequestedSeed = RunSeedDeriver.Derive(masterSeed,RunSeedDeriver.CombatMapTag),

                TerritoryRequestedSeed =RunSeedDeriver.Derive(masterSeed,RunSeedDeriver.TerritoryTag)
            };

            return data;
        }

        /// <summary>
        /// 저장된 Run 시드 데이터를 복원한다.
        /// </summary>
        public void Restore(RunSeedData savedData)
        {
            if (savedData == null)
            {
                throw new ArgumentNullException(nameof(savedData));
            }

            if (savedData.SeedVersion != RunSeedDeriver.CurrentVersion)
            {
                throw new InvalidOperationException($"지원하지 않는 시드 버전입니다. 저장 버전: {savedData.SeedVersion}, " +
                    $"현재 버전:{RunSeedDeriver.CurrentVersion}"
                );
            }

            data = savedData;
        }

        public void RecordCombatMapUsedSeed(int usedSeed)
        {
            EnsureInitialized();

            data.CombatMapUsedSeed = usedSeed;
        }

        public void RecordTerritoryUsedSeed(int usedSeed)
        {
            EnsureInitialized();

            data.TerritoryUsedSeed = usedSeed;
        }

        private void EnsureInitialized()
        {
            if (data == null)
            {
                throw new InvalidOperationException("Run 시드가 초기화되지 않았습니다.");
            }
        }

        private static int GenerateRandomMasterSeed()
        {
            byte[] bytes = new byte[4];

            using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            int result = BitConverter.ToInt32(bytes, 0) & int.MaxValue;

            // 0은 Inspector override 등의
            // 특수값으로 사용할 수 있으므로 제외한다.
            return result == 0? 1 : result;
        }
    }
}