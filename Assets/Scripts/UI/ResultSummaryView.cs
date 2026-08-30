using NorthLand.Core;
using TMPro;
using UnityEngine;

namespace NorthLand.UI
{
    /// 결과창에 이번 판의 요약 정보(현재 시드·도달 웨이브)를 채우는 뷰.
    ///
    /// **값을 스스로 갱신하지 않는다.** 결과창은 판이 끝난 시점의 스냅샷이라 이후 값이 변할 일이
    /// 없고, 매 프레임 폴링하면 `Time.timeScale`이 0인 화면에서 의미 없는 일을 계속하게 된다.
    /// <see cref="ResultPanelAnimator"/>가 패널을 띄우면서 <see cref="Bind"/>를 한 번 부른다.
    ///
    /// 소스가 없어도 결과창 자체는 반드시 떠야 하므로(여기서 예외가 나면 플레이어가 화면에 갇힌다)
    /// 모든 조회 실패는 <see cref="Unavailable"/> 표기로 떨어뜨린다.
    public class ResultSummaryView : MonoBehaviour
    {
        /// 값을 못 읽었을 때의 표기. 빈 문자열로 두면 라벨만 남아 "버그로 지워진 것"처럼 보인다.
        private const string Unavailable = "-";

        [Header("표시 대상")]
        [SerializeField]
        [Tooltip("현재 시드 숫자가 들어갈 텍스트.")]
        private TMP_Text seedValue;

        [SerializeField]
        [Tooltip("도달 웨이브 숫자가 들어갈 텍스트.")]
        private TMP_Text waveValue;

        [Header("소스")]
        [SerializeField]
        [Tooltip("마스터 시드를 제공한다. 비우면 씬에서 찾는다.")]
        private RunBootstrapper runBootstrapper;

        /// 지금 화면에 표시할 값을 한 번 읽어 채운다.
        public void Bind()
        {
            SetText(seedValue, ResolveSeed());
            SetText(waveValue, ResolveWave());
        }

        private string ResolveSeed()
        {
            if (runBootstrapper == null)
            {
                // MonsterSpawnWaveProvider와 같은 방식의 폴백. 배선이 빠져도 화면은 뜨게 한다.
                runBootstrapper = FindFirstObjectByType<RunBootstrapper>();
            }

            if (runBootstrapper == null)
            {
                Debug.LogWarning($"[{nameof(ResultSummaryView)}] RunBootstrapper를 찾지 못해 시드를 표시하지 못했습니다.", this);

                return Unavailable;
            }

            // 초기화 전에는 MasterSeed가 0을 돌려준다. 그대로 쓰면 "시드 0"이라는
            // 있지도 않은 값을 플레이어에게 보여주게 되므로 미확정과 구분한다.
            RunSeedContext context = runBootstrapper.SeedContext;

            if (context == null || !context.IsInitialized)
            {
                return Unavailable;
            }

            return runBootstrapper.MasterSeed.ToString();
        }

        private string ResolveWave()
        {
            DayNightManager dayNight = DayNightManager.Instance;

            if (dayNight == null)
            {
                return Unavailable;
            }

            // CurrentWave는 "지금 진행 중인 웨이브 번호"(1부터)다. 승리든 패배든
            // 결과가 확정된 순간의 이 값이 곧 "몇 웨이브에서 끝났는가"가 된다.
            return dayNight.CurrentWave.ToString();
        }

        private void SetText(TMP_Text label, string value)
        {
            if (label == null)
            {
                Debug.LogWarning($"[{nameof(ResultSummaryView)}] 표시 대상 텍스트가 배선되지 않았습니다.", this);

                return;
            }

            label.text = value;
        }
    }
}
