using System.Collections.Generic;
using NorthLand.Combat;
using UnityEngine;

namespace NorthLand.UI
{
    /// 살아 있는 모든 몬스터의 체력바를 담는 **공용 월드 스페이스 캔버스**(#447 · WL-055).
    ///
    /// 왜 캔버스 하나인가:
    /// · **드로우콜** — 몬스터마다 캔버스를 두면 캔버스끼리는 절대 배칭되지 않아 마릿수만큼 드로우콜이
    ///   붙는다. 한 캔버스에 모으면 배경·필·눈금이 전부 흰 텍스처라 **합계 1 배치**로 묶인다.
    /// · **스케일 독립** — 바가 몬스터 트랜스폼 밑에 없으므로 프리팹 루트 스케일이 바 크기에 아예
    ///   닿지 않는다. 코드로 스케일을 되돌리는 보정이 필요 없다(= 보정을 빠뜨릴 자리도 없다).
    /// · **빌보드 1회** — 캔버스 루트만 카메라를 향하게 돌리면 아래 바 전부가 따라 돈다.
    ///
    /// 씬 배선이 없다. <see cref="Enemy.Spawned"/>를 정적으로 구독해 첫 몬스터가 나오는 순간 자신을
    /// 만든다 — 씬에 오브젝트를 두면 "새 씬에서 빠뜨리면 무증상"이라는 WL-055의 실패 통로가 프리팹에서
    /// 씬으로 옮겨갈 뿐이기 때문이다. 튜닝 값(폭·높이·여유·눈금 단위)은 전부 Resources의 바 프리팹이 쥔다.
    ///
    /// ⚠ 알려진 한계: 한 캔버스 안에서는 계층 순서대로 그려지므로, 화면에서 두 바가 겹치면 먼 쪽이
    ///   가까운 쪽 위에 그려질 수 있다. 바끼리의 겹침에 한정되고 지형 가림(ZTest)은 정상이다.
    public sealed class MonsterHealthBarLayer : MonoBehaviour
    {
        // 정본은 `Assets/Resources/UI/MonsterHealthBar.prefab`(메인 저장소)이다.
        //
        // **Resources로 집어오는 이유**: 씬·프리팹 어디에도 참조를 심지 않기 위해서다. 씬에 배선을 두면
        // "새 씬에서 빠뜨리면 무증상"이라는 WL-055의 실패 통로가 프리팹에서 씬으로 옮겨갈 뿐이다.
        const string k_BarResourcePath = "UI/MonsterHealthBar";

        // 캔버스 1px = 0.01 월드 단위. 바의 월드 크기는 프리팹 sizeDelta × 이 값이며,
        // **모든 몬스터가 같은 값을 쓴다**(폭이 몬스터별 성질이 아니라는 것이 이 이슈의 요구사항).
        const float k_CanvasScale = 0.01f;

        static MonsterHealthBarLayer instance;
        static MonsterHealthBar barPrefab;
        static bool prefabLoadFailed;
        static bool quitting;

        readonly List<MonsterHealthBar> live = new List<MonsterHealthBar>();
        readonly Stack<MonsterHealthBar> pool = new Stack<MonsterHealthBar>();

        Camera cachedCamera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // 도메인 리로드를 끈 설정에서도 중복 구독이 되지 않도록 해제를 먼저 건다.
            Enemy.Spawned -= HandleEnemySpawned;
            Enemy.Spawned += HandleEnemySpawned;

            Application.quitting -= HandleQuitting;
            Application.quitting += HandleQuitting;

            quitting = false;
        }

        static void HandleQuitting()
        {
            quitting = true;
        }

        static void HandleEnemySpawned(Enemy enemy)
        {
            if (quitting || enemy == null)
            {
                return;
            }

            MonsterHealthBarLayer layer = EnsureInstance();

            if (layer != null)
            {
                layer.Attach(enemy);
            }
        }

        static MonsterHealthBarLayer EnsureInstance()
        {
            // 씬이 바뀌면 캔버스도 함께 사라지므로(DontDestroyOnLoad를 쓰지 않는다) 매번 확인한다.
            if (instance != null)
            {
                return instance;
            }

            if (prefabLoadFailed)
            {
                return null;
            }

            if (barPrefab == null)
            {
                barPrefab = Resources.Load<MonsterHealthBar>(k_BarResourcePath);

                if (barPrefab == null)
                {
                    prefabLoadFailed = true;
                    Debug.LogError(
                        $"[몬스터 체력바] Resources/{k_BarResourcePath} 프리팹을 찾지 못해 체력바가 표시되지 않습니다.");

                    return null;
                }
            }

            var go = new GameObject(nameof(MonsterHealthBarLayer), typeof(RectTransform), typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = UILayer.MonsterHealthBar;

            // GraphicRaycaster를 붙이지 않는다 — 체력바는 입력을 받지 않는다(전부 raycastTarget=false).
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = Vector2.one;
            rect.localScale = Vector3.one * k_CanvasScale;

            instance = go.AddComponent<MonsterHealthBarLayer>();
            return instance;
        }

        void Attach(Enemy enemy)
        {
            MonsterHealthBar bar = pool.Count > 0 ? pool.Pop() : Instantiate(barPrefab);

            Transform t = bar.transform;
            t.SetParent(transform, false);

            // 캔버스 루트가 0.01 스케일이라, 월드 스케일을 보존하는 부모 지정 경로를 타면 바가 100배로
            // 부풀어 오른다. 크기는 프리팹 sizeDelta 하나가 정하는 값이므로 여기서 확정으로 되돌린다.
            t.localScale = Vector3.one;
            t.localRotation = Quaternion.identity;

            bar.gameObject.SetActive(true);
            bar.Bind(enemy, ComputeAnchorHeight(enemy, bar.TopMargin));
            bar.Follow(ResolveCamera());

            live.Add(bar);
        }

        /// 바를 띄울 높이 = 몬스터 렌더러 상단 − 루트 Y + 여유. 프리팹에 앵커 오브젝트를 두지 않는 이유는
        /// 그것이 결국 몬스터마다 손으로 맞추는 값이 되어(그리고 신규 몬스터에서 누락되어) WL-055가
        /// 앵커로 자리만 옮기기 때문이다. 아트가 바뀌면 이 계산은 저절로 따라간다.
        ///
        /// 파티클·트레일은 제외한다 — 이펙트 바운즈는 몸통과 무관하게 커서 바가 하늘로 뜬다.
        static float ComputeAnchorHeight(Enemy enemy, float margin)
        {
            Renderer[] renderers = enemy.GetComponentsInChildren<Renderer>(true);
            float top = float.NegativeInfinity;

            foreach (Renderer r in renderers)
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                {
                    continue;
                }

                if (r.bounds.max.y > top)
                {
                    top = r.bounds.max.y;
                }
            }

            if (float.IsNegativeInfinity(top))
            {
                // 렌더러가 없다(에디터 테스트용 빈 오브젝트 등). 여유값만으로 띄운다.
                return margin;
            }

            return top - enemy.transform.position.y + margin;
        }

        void LateUpdate()
        {
            Camera cam = ResolveCamera();

            if (cam != null)
            {
                // 빌보드: 캔버스 루트 한 번만 돌리면 아래 바 전부가 카메라를 향한다.
                transform.rotation = cam.transform.rotation;
            }

            for (int i = live.Count - 1; i >= 0; i--)
            {
                MonsterHealthBar bar = live[i];

                if (bar == null)
                {
                    live.RemoveAt(i);
                    continue;
                }

                if (bar.Follow(cam))
                {
                    continue;
                }

                bar.Release();
                bar.gameObject.SetActive(false);
                live.RemoveAt(i);
                pool.Push(bar);
            }
        }

        Camera ResolveCamera()
        {
            if (cachedCamera == null)
            {
                cachedCamera = Camera.main;
            }

            return cachedCamera;
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            live.Clear();
            pool.Clear();
        }
    }
}
