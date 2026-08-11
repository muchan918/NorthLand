using System;

namespace NorthLand.Core
{
    /// <summary>
    /// 플레이어 세이브 슬롯을 대표하는 기본 정보.
    /// 설정, 업적, Run 데이터는 별도 파일로 관리한다.
    /// </summary>
    [Serializable]
    public sealed class PlayerData
    {
        public int version = 1;

        public string playerId;

        public long createdAt;

        public long lastPlayedAt;

        public string playerName;

        public static PlayerData Create(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
            {
                throw new ArgumentException("플레이어 이름이 비어 있습니다.",nameof(playerName));
            }

            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            return new PlayerData
            {
                playerId = Guid.NewGuid().ToString("N"),
                playerName = playerName.Trim(),
                createdAt = currentTime,
                lastPlayedAt = currentTime
            };
        }

        public void UpdateLastPlayedAt()
        {
            lastPlayedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}