using System;
using System.Text;

namespace NorthLand.Core
{
    /// <summary>
    /// 마스터 시드로부터 시스템별 시드를 파생한다.
    /// 호출 순서와 플랫폼에 영향을 받지 않는다.
    /// </summary>
    public static class RunSeedDeriver
    {
        // 시드 파생 규칙 또는 시드로 복원되는 맵 생성 설정이 변경되면 올린다.
        public const int CurrentVersion = 2;

        public const string CombatMapTag = "CombatMap";

        public static int Derive(int masterSeed,string systemTag)
        {
            if (string.IsNullOrWhiteSpace(systemTag))
            {
                throw new ArgumentException("시스템 태그가 비어 있습니다.",nameof(systemTag));
            }

            unchecked
            {
                // 고정 FNV-1a 해시.
                uint hash = 2166136261u;

                MixByte(ref hash,(byte)masterSeed);

                MixByte(ref hash,(byte)(masterSeed >> 8));

                MixByte(ref hash,(byte)(masterSeed >> 16));

                MixByte(ref hash,(byte)(masterSeed >> 24));

                byte[] tagBytes = Encoding.UTF8.GetBytes(systemTag);

                foreach (byte value in tagBytes)
                {
                    MixByte(ref hash, value);
                }

                // Inspector override에서 0을
                // 특수값으로 사용할 수 있도록 피한다.
                int result = (int)(hash & 0x7FFFFFFF);

                return result == 0? 1: result;
            }
        }

        private static void MixByte(ref uint hash,byte value)
        {
            hash ^= value;
            hash *= 16777619u;
        }
    }
}