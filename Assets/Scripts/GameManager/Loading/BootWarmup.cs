using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization.Settings;

namespace NorthLand.Core
{
    /// <summary>
    /// 인게임 첫 사용 시점에 물던 비용을 로딩 구간으로 앞당기는 워밍업 모음.
    /// 항목별 실측 근거는 <c>Docs/Core/LoadingScene.md</c> §4에 있다.
    ///
    /// 여기 있는 것은 전부 <b>씬과 무관하게 미리 끝낼 수 있는 것</b>뿐이다.
    /// 씬이 있어야 성립하는 것(타일 Instantiate·주민 스폰)은 커튼 뒤 프레임 분산의 몫이지
    /// 워밍업이 아니다 — 같은 문서 §4 Tier 3.
    ///
    /// 전부 멱등이다. 로딩을 두 번 지나도(타이틀 → 게임 → 타이틀 → 게임) 두 번째는 거의 공짜다.
    /// </summary>
    public static class BootWarmup
    {
        /// 프로젝트 표준 반투명 언릿. <c>RangeCircle</c>·<c>BeamAction</c>·<c>GrainSwarm</c>·
        /// <c>TowerDissolveEffect</c>·<c>TowerPlacer</c>가 전부 이 하나를 <c>Shader.Find</c>로 찾는다 —
        /// 그래서 여기서 한 번 찾아 두면 다섯 곳이 같이 해소된다.
        private const string k_SharedUnlitShader = "Sprites/Default";

        /// <see cref="TowerAsset"/>이 사는 Resources 폴더. 이 경로 문자열은 이미
        /// <c>RunSaveManager.Towers</c>·<c>DebugTowerSection</c>·<c>FusionTowerCodexUI</c>에도 있다 —
        /// 상수 단일화는 별건이므로 여기서는 기존 관행을 따른다.
        private const string k_TowerFolder = "ScriptableObjects/Towers";

        // 몬스터 체력바 프리팹(#447). 경로 정본은 MonsterHealthBarLayer.k_BarResourcePath와 같아야 한다.
        private const string k_MonsterHealthBarPath = "UI/MonsterHealthBar";

        /// 찾아 둔 공유 셰이더. 두 번째 로딩에서 <c>Shader.Find</c>를 또 때리지 않기 위한 것이며,
        /// 소비처는 각자 <c>Shader.Find</c>를 부른다(그때는 이미 로드돼 있어 싸다).
        private static Shader s_sharedUnlit;

        private static bool s_dataTablesTouched;

        /// <summary>
        /// Localization 초기화를 끝내고 String Table을 전부 미리 적재한다.
        ///
        /// ⚠ 이것이 없으면 <c>LocalizationHelper.Get</c>의 첫 호출이
        /// <c>GetTableAsync(...).WaitForCompletion()</c>으로 <b>메인 스레드를 동기 블로킹</b>한다
        /// (<c>LocalizationHelper.cs:49</c>). 게임 도중 아무 툴팁이나 처음 뜨는 순간이 그 시점이다.
        /// </summary>
        public static async UniTask WarmLocalizationAsync(CancellationToken cancellationToken)
        {
            // InitializationOperation은 접근하는 순간 시작된다. UniTask.Addressables 어셈블리에
            // 의존하지 않도록 완료 폴링으로 기다린다 — 로딩 구간이라 프레임당 1회 검사로 충분하다.
            await UniTask.WaitUntil(
                () => LocalizationSettings.InitializationOperation.IsDone,
                cancellationToken: cancellationToken);

            LocalizationHelper.Warm(
                LocalizationHelper.k_DefaultTable,
                LocalizationHelper.k_BuildingsTable,
                LocalizationHelper.k_TowersTable,
                LocalizationHelper.k_EnemiesTable,
                LocalizationHelper.k_TerritoriesTable,
                LocalizationHelper.k_RewardsTable,
                LocalizationHelper.k_SkillsTable,
                LocalizationHelper.k_TutorialTable);
        }

        /// <summary>
        /// CSV 데이터 테이블 4종을 적재한다(합 11.4ms 실측 — §3.1).
        ///
        /// 비용 절감이 목적이 아니다. <c>DataTableManager</c>는 static 생성자에서 적재하므로
        /// <b>"누가 처음 만지느냐"가 초기화 시점을 정한다</b> — 지금은 그게 비결정적이다.
        /// 여기서 한 번 만져 시점을 로딩 구간으로 고정하는 것이 목적이다.
        /// </summary>
        public static void WarmDataTables()
        {
            if (s_dataTablesTouched) return;

            // 어느 테이블이든 한 번 조회하면 static 생성자가 돌아 4종이 전부 적재된다.
            DataTableManager.Get<ResourceTable>("ResourceTable");

            s_dataTablesTouched = true;
        }

        /// <summary>
        /// 타워 SO와 합성 레시피를 적재한다. <b>이 워밍업의 최대 항목이다(콜드 586.84ms 실측 — §3.2).</b>
        ///
        /// 비용의 정체는 SO 20개가 아니라 그것들이 물고 있는 <c>TowerPrefab</c>·<c>GhostPrefab</c> 40개와
        /// 아이콘·메시·머티리얼·VFX다(웜이 0.68ms인 것이 근거). 그중 <b>13종은 GameScene이 참조하지
        /// 않는 합성 결과 타워</b>라 <c>LoadSceneAsync</c>가 올려 주지 않는다 — 합성 패널·도감·이어하기
        /// 복원 중 무엇이든 처음 열리는 순간 인게임에서 한꺼번에 올라온다(§3.3).
        /// </summary>
        public static void WarmTowerAssets()
        {
            // 레시피와 타워를 둘 다 만진다. TowerRecipe.Result / MaterialEntry.Tower가 TowerAsset
            // 직접 참조라 서로를 끌어오지만, 레시피에 등장하지 않는 타워는 레시피만으로 안 올라온다.
            Resources.LoadAll<TowerAsset>(k_TowerFolder);

            _ = TowerRecipeCatalog.All;
        }

        /// <summary>
        /// 첫 사용 때 만들던 공유 시각 자원을 미리 만든다.
        /// 공유 언릿 셰이더 1종 + <see cref="GrainSwarm"/>의 절차적 알갱이 텍스처·쿼드 메시
        /// + 몬스터 체력바 프리팹(#447).
        /// </summary>
        public static void WarmSharedVisuals()
        {
            // 체력바 프리팹은 첫 몬스터가 스폰되는 프레임에 Resources.Load로 동기 적재된다
            // (MonsterHealthBarLayer). 하필 웨이브 시작 프레임이라 눈에 띄는 자리이고,
            // 씬과 무관하게 미리 끝낼 수 있는 항목이라 이 워밍업의 조건(§LoadingScene.md)에 맞는다.
            // 반환값을 버려도 Unity의 리소스 캐시에 남아 나중 Load가 웜이 된다. 멱등이다.
            Resources.Load<NorthLand.UI.MonsterHealthBar>(k_MonsterHealthBarPath);

            s_sharedUnlit ??= Shader.Find(k_SharedUnlitShader);

            if (s_sharedUnlit == null)
            {
                Debug.LogWarning(
                    $"[Loading] 공유 셰이더 '{k_SharedUnlitShader}'를 찾지 못했습니다. " +
                    "사거리 원·고스트·합성 연출이 첫 사용 시점에 셰이더를 다시 찾습니다.");
            }

            NorthLand.Combat.GrainSwarm.PrewarmShared();
        }
    }
}
