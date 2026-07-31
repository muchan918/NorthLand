using System.Collections.Generic;
using UnityEngine;

// 아웃라인 shell이 쓰는 "스무스 노멀 사본 메시" 매핑표(#213, Docs/Core/InteractionOutline.md §6.4).
//
// 왜 필요한가: 대상 모델이 전부 하드(스플릿) 노멀 로우폴리라 인버티드 헐을 그대로 씌우면 면 단위로 찢어져
// 게임 줌에서 점선 프린지가 된다. 정점 위치를 공유하는 노멀을 평균해 uv3(TEXCOORD2)에 넣고 셰이더의
// DR_OUTLINE_SMOOTH_NORMALS를 켜야 연속된 테두리가 나온다. 원근에서 이 노이즈를 지우던
// _OutlineDepthOffset은 게임 카메라가 직교라서 쓸 수 없다(§3.4) — 그래서 우회로가 아니라 전제다.
//
// 왜 런타임 계산이 아닌가: 대상 메시 대부분이 isReadable=false라 런타임에서 정점을 읽을 수 없고,
// FlatKit의 스무딩 유틸은 Editor 전용 asmdef다. 그래서 에디터에서 미리 굽고(OutlineSmoothMeshBaker)
// 그 결과를 이 표에 기록한다. 사본은 벤더 트리가 아니라 @NorthLand 아래에 만든다.
//
// 왜 Resources인가: 타워 마커(TowerGroupSelectable)가 런타임 AddComponent라 인스펙터 배선을 받을 수
// 없다. DataTable CSV와 같은 Resources.Load 규약을 따른다.
[CreateAssetMenu(fileName = "OutlineSmoothMeshRegistry", menuName = "Scriptable Objects/OutlineSmoothMeshRegistry")]
public class OutlineSmoothMeshRegistry : ScriptableObject
{
    /// Resources 기준 경로(확장자·Resources 접두 없음). 베이커도 이 상수를 쓴다.
    public const string ResourcePath = "Outline/OutlineSmoothMeshRegistry";

    [Tooltip("베이크 대상 프리팹. 메뉴 NorthLand/Outline/1로 자동 수집하고, 필요 없는 항목은 손으로 지우면 된다.")]
    public List<GameObject> TargetPrefabs = new();

    [Tooltip("원본 메시 → 스무스 사본. 베이커가 채운다(손으로 편집하지 말 것).")]
    public List<Entry> Entries = new();

    // 전역 일반명 충돌(단일 Assembly-CSharp, WL-062 축)을 피해 중첩한다.
    [System.Serializable]
    public class Entry
    {
        public Mesh Source;
        public Mesh Smooth;
    }

    private static OutlineSmoothMeshRegistry s_instance;
    private static bool s_loadAttempted;
    private Dictionary<Mesh, Mesh> _lookup;

    /// 런타임 조회용 인스턴스. 에셋이 없으면 null이며, 호출부는 원본 메시로 폴백한다(아웃라인이 끊겨 보이지만 동작은 유지).
    public static OutlineSmoothMeshRegistry Instance
    {
        get
        {
            if (s_instance != null) return s_instance;
            if (s_loadAttempted) return null; // 없는 에셋을 매 호출마다 다시 찾지 않는다
            s_loadAttempted = true;

            s_instance = Resources.Load<OutlineSmoothMeshRegistry>(ResourcePath);
            if (s_instance == null)
            {
                Debug.LogWarning($"[아웃라인] 스무스 메시 레지스트리를 찾지 못했습니다(Resources/{ResourcePath}). " +
                                 "메뉴 NorthLand/Outline 으로 베이크하세요 — 그때까지 아웃라인이 끊겨 보입니다.");
            }
            return s_instance;
        }
    }

    /// 원본 메시에 대응하는 스무스 사본. 등록돼 있지 않으면 **원본을 그대로** 반환한다(표시 실패보다 품질 저하를 택한다).
    public Mesh Resolve(Mesh source)
    {
        if (source == null) return null;

        BuildLookup();
        return _lookup.TryGetValue(source, out var smooth) && smooth != null ? smooth : source;
    }

    /// 표가 바뀐 뒤(베이크 직후) 호출 — 다음 조회에서 캐시를 다시 만든다.
    public void InvalidateLookup() => _lookup = null;

    private void BuildLookup()
    {
        if (_lookup != null) return;

        _lookup = new Dictionary<Mesh, Mesh>(Entries.Count);
        foreach (var e in Entries)
        {
            if (e == null || e.Source == null || e.Smooth == null) continue;
            _lookup[e.Source] = e.Smooth;
        }
    }
}
