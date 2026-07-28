using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 타워 전용 호버 툴팁 뷰(#141). 건물과 공유하는 범용 <see cref="TooltipUI"/>(헤더+본문 텍스트뿐)와 달리
/// 타워 정보에 맞춘 독립 슬롯(아이콘/이름/스탯/코스트)을 갖는다.
/// <br/>
/// <b>씬 배치(권장)</b>: <see cref="TooltipUI"/>와 같은 계보로, 인스펙터 슬롯을 채워 <c>UICanvas</c>의
/// 마지막 자식으로 배치한다(HUD 위·모달 아래 — Docs/Core/UIZOrder.md §4).
/// (주의) 루트 오브젝트는 '활성'으로 둬야 Awake가 실행되어 Instance가 등록된다. 숨김은 자식
/// <c>_panel</c> 토글로 처리하므로 루트를 미리 꺼두지 말 것.
/// <br/>
/// <b>폴백</b>: 슬롯이 비어 있으면 <see cref="BuildFallback"/>이 계층을 코드로 만든다. 이때도 자체 캔버스를
/// 만들지 않고 씬의 HUD 캔버스(<see cref="UILayer.Hud"/>) 아래로 붙는다 — 테스트 씬처럼 HUD 캔버스가
/// 아예 없을 때만 표준 sortingOrder로 캔버스를 생성한다.
/// <see cref="TowerTooltipSource"/>가 버튼 호버를 감지해 <see cref="Show"/>를 호출한다.
/// </summary>
public class TowerTooltipView : MonoBehaviour
{
    public static TowerTooltipView Instance { get; private set; }

    [Header("표시 대상")]
    [SerializeField] Canvas _canvas;           // scaleFactor 계산용(루트 캔버스). 비면 부모에서 자동 조회
    private GameObject _panel;        // 보임/숨김 토글 대상 (루트가 아니라 이 자식)
    private RectTransform _panelRect; // 배치 대상. 비면 _panel에서 자동 조회


    private Image _icon;
    private TextMeshProUGUI _nameText;   
    private TextMeshProUGUI _descText;
    private TextMeshProUGUI _statsText;
    private TextMeshProUGUI _costText;
 
    [Header("배치")]
    // 호버한 버튼의 위쪽 가장자리 기준 오프셋(x=수평 이동, y=버튼과의 간격).
    [SerializeField] Vector2 _buttonOffset = new(0f, 8f);

    private ResourceTable _resourceTable;
    private bool _ready; // 슬롯이 온전한가 — Show가 외부에서 직접 불리므로 enabled 대신 이 플래그로 가드

    /// <summary>
    /// 인스턴스를 반환한다. 씬에 배치된 뷰를 최우선으로 쓰고, 없을 때만 자체 생성한다. 첫 호버 시 호출된다.
    /// </summary>
    public static TowerTooltipView EnsureExists()
    {
        if (Instance != null) return Instance;

        // 씬에 배치돼 있는데 루트가 비활성이면 Awake가 아직 안 돌아 Instance가 비어 있다.
        // Include로 찾아 활성화하면 그 순간 Awake가 Instance를 등록한다 — 중복 생성 방지.
        var existing = FindFirstObjectByType<TowerTooltipView>(FindObjectsInactive.Include);
        if (existing != null)
        {
            if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
            return Instance != null ? Instance : existing; // 부모가 비활성이면 Awake가 여전히 안 돈다
        }

        var go = new GameObject("TowerTooltipView (auto)", typeof(RectTransform));
        return go.AddComponent<TowerTooltipView>(); // Awake에서 BuildFallback + Instance 등록
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (_panel == null) BuildFallback(); // 씬 와이어링이 없을 때만 코드로 생성

        _panelRect ??= _panel != null ? _panel.GetComponent<RectTransform>() : null;
        _canvas ??= GetComponentInParent<Canvas>();
        if (_canvas != null) _canvas = _canvas.rootCanvas; // scaleFactor는 루트 기준

        _ready = _panel != null && _panelRect != null && _nameText != null
                 && _descText != null && _statsText != null && _costText != null;
        if (!_ready)
        {
            Debug.LogError("TowerTooltipView: 필수 슬롯(_panel/_panelRect/_nameText/_descText/_statsText/_costText) 미할당", this);
            return; // Hide() 호출 전에 빠져 첫 프레임 NRE 방지
        }

        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>타워 정보를 채워 <paramref name="anchor"/>(호버한 버튼) 바로 위에 툴팁을 표시한다. tower가 null이면 숨긴다.</summary>
    public void Show(TowerAsset tower, RectTransform anchor)
    {
        if (!_ready) return;
        if (tower == null) { Hide(); return; }

        _nameText.text = ResolveHeader(tower);
        string desc = ResolveDescription(tower);
        _descText.text = desc;
        _descText.gameObject.SetActive(!string.IsNullOrEmpty(desc)); // 설명 없으면 빈 줄 안 남기고 접음
        _statsText.text = BuildStats(tower);
        _costText.text = BuildCost(tower);

        // 아이콘: TowerAsset에 Sprite 필드가 생기면 여기서 `_icon.sprite = tower.Icon;` 로 바인딩한다.
        // 지금은 슬롯만 존재(플레이스홀더) — 필드가 없으므로 자리만 유지한다.

        _panel.SetActive(true);
        if (anchor != null) PositionAbove(anchor);
    }

    public void Hide()
    {
        if (_panel != null) _panel.SetActive(false);
    }

    // ----- 폴백 UI 구성(런타임 코드 생성) -----
    // 씬 와이어링이 없을 때만 실행된다. 씬에 배치했다면 이 경로는 전혀 타지 않는다.

    private void BuildFallback()
    {
        Transform root = ResolveRoot();
        TMP_FontAsset font = ResolveFont();

        // 패널(배경 + 세로 레이아웃 + 자동 크기). pivot 하단중앙 → 버튼 위로 자라남.
        _panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _panel.transform.SetParent(root, false);
        _panelRect = _panel.GetComponent<RectTransform>();
        _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0f, 0f);
        _panelRect.pivot = new Vector2(0.5f, 0f);
        var bg = _panel.GetComponent<Image>();
        bg.color = new Color(0.06f, 0.07f, 0.10f, 0.92f);
        bg.raycastTarget = false;
        var vlg = _panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(12, 12, 10, 10);
        vlg.spacing = 6;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;
        var fit = _panel.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 헤더(아이콘 + 이름)
        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(_panel.transform, false);
        var hlg = header.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        // 아이콘 플레이스홀더(Image 슬롯만 — TowerAsset Sprite 필드는 나중에)
        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconGO.transform.SetParent(header.transform, false);
        _icon = iconGO.GetComponent<Image>();
        _icon.color = new Color(0.30f, 0.32f, 0.40f, 1f);
        _icon.raycastTarget = false;
        var le = iconGO.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = 40;
        le.preferredHeight = le.minHeight = 40;

        _nameText = MakeText("Name", header.transform, font, 22f, FontStyles.Bold, Color.white);

        // 설명(#141 완료조건): 여러 줄일 수 있어 줄바꿈 허용 + 폭 제한(다른 줄은 NoWrap이라 이 폭이 패널 폭을 잡음).
        _descText = MakeText("Desc", _panel.transform, font, 16f, FontStyles.Normal, new Color(0.75f, 0.78f, 0.85f));
        _descText.textWrappingMode = TextWrappingModes.Normal;
        _descText.gameObject.AddComponent<LayoutElement>().preferredWidth = 260f;

        _statsText = MakeText("Stats", _panel.transform, font, 18f, FontStyles.Normal, new Color(0.85f, 0.90f, 1f));
        _costText = MakeText("Cost", _panel.transform, font, 18f, FontStyles.Normal, new Color(1f, 0.92f, 0.60f));
    }

    /// <summary>
    /// 폴백 계층을 담을 부모를 정한다. 씬의 HUD 캔버스(<see cref="UILayer.Hud"/>) 아래 마지막 자식으로
    /// 들어가므로 일반 HUD보다 위·보상/결과 모달보다 아래에 그려진다(UIZOrder.md §3·§4).
    /// HUD 캔버스가 없는 씬(테스트 씬 등)에서만 자체 캔버스를 만들되 sortingOrder는 표준값을 쓴다.
    /// </summary>
    private Transform ResolveRoot()
    {
        // 이름(GameObject.Find("UICanvas")) 대신 표준 sortingOrder로 식별한다 — 이름 변경에 안 깨진다.
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (c.sortingOrder != UILayer.Hud) continue;

            _canvas = c;
            transform.SetParent(c.transform, false); // 마지막 sibling = UICanvas 내 최상단
            var rt = transform as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            // 스케일은 UICanvas의 CanvasScaler(ScaleWithScreenSize 1920×1080)를 그대로 상속한다.
            return transform;
        }

        // HUD 캔버스가 없는 씬 — 표준 레이어를 지켜 자체 캔버스를 만든다(임의의 큰 값 금지).
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = UILayer.Hud;
        // 게임의 다른 캔버스와 스케일을 맞춘다(전부 ScaleWithScreenSize 1920×1080) — 고해상도/모바일에서
        // 버튼만 커지고 툴팁 텍스트가 상대적으로 작아지는 어긋남 방지.
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        // GraphicRaycaster는 붙이지 않는다 — 툴팁은 입력을 받지 않는다(패널도 raycastTarget off).
        return transform;
    }

    private static TextMeshProUGUI MakeText(string n, Transform parent, TMP_FontAsset font, float size, FontStyles style, Color col)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = col;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    // 씬에 이미 쓰이는 TMP 폰트를 재사용(한글 지원 폰트 일치). 없으면 TMP 기본 폰트.
    private static TMP_FontAsset ResolveFont()
    {
        var any = FindFirstObjectByType<TextMeshProUGUI>();
        if (any != null && any.font != null) return any.font;
        return TMP_Settings.defaultFontAsset;
    }

    // 호버한 버튼(anchor)의 위쪽 가장자리 중앙에 툴팁 하단 중앙을 맞춰 '버튼 위에 고정' 배치한다.
    // (Screen Space - Overlay 기준: RectTransform 월드 좌표 = 화면 픽셀.) 화면 밖으로 나가지 않게 clamp.
    private void PositionAbove(RectTransform anchor)
    {
        // ContentSizeFitter 결과 크기를 이번 프레임에 확정해야 clamp 계산이 정확하다(레이아웃 타이밍).
        LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);

        var corners = new Vector3[4]; // 0=좌하 1=좌상 2=우상 3=우하
        anchor.GetWorldCorners(corners);
        Vector2 topCenter = (corners[1] + corners[2]) * 0.5f; // 버튼 상단 중앙(화면 px)

        Vector2 pos = topCenter + _buttonOffset;
        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        Vector2 size = _panelRect.rect.size * scale;
        float half = size.x * 0.5f;
        pos.x = Mathf.Clamp(pos.x, half, Mathf.Max(half, Screen.width - half));
        pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, Screen.height - size.y));
        _panelRect.position = pos;
    }

    // ----- 내용 조립 (변경 없음) -----

    // 헤더 = 타워 이름 (+ 역할, 있으면). Data 미채움 시 TowerID로 폴백.
    // BuildingTooltipSource의 "이름 - 역할" 표기와 같은 계열.
    private string ResolveHeader(TowerAsset t)
    {
        if (t.Data == null || string.IsNullOrEmpty(t.Data.NameKey)) return t.TowerID;

        string name = LocalizationHelper.Get(LocalizationHelper.k_TowersTable, t.Data.NameKey);
        if (!string.IsNullOrEmpty(t.Data.RoleKey))
        {
            string role = LocalizationHelper.Get(LocalizationHelper.k_TowersTable, t.Data.RoleKey);
            if (!string.IsNullOrEmpty(role)) return $"{name} - {role}";
        }
        return name;
    }

    // 설명(#141): TowerData.DescriptionKey를 Towers 테이블에서 pull. Magic 타워는 공통 공격 스탯이
    // 없어 이 설명이 실질적 정보의 핵심(poison/slow/haste 구분 근거). 없으면 빈 문자열(호출부가 접음).
    private string ResolveDescription(TowerAsset t)
    {
        if (t.Data == null || string.IsNullOrEmpty(t.Data.DescriptionKey)) return string.Empty;
        return LocalizationHelper.Get(LocalizationHelper.k_TowersTable, t.Data.DescriptionKey);
    }

    // 공격력/사거리/공속을 game.tower.* 로컬라이즈 라벨로 조합(Tower.BuildStatsText와 동일 규칙).
    // Magic 타워는 공통 공격 스탯이 없어 오라 반경(=사거리)으로 대체 표기한다.
    private string BuildStats(TowerAsset t)
    {
        TowerAsset.AttackFields atk = AttackOf(t);
        if (atk != null)
        {
            string d = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, "game.tower.attack_damage");
            string r = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, "game.tower.attack_range");
            string s = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, "game.tower.attack_speed");
            float rate = atk.AttackInterval > 0f ? 1f / atk.AttackInterval : 0f;
            return $"{d}: {atk.AttackDamage:0.#}\n{r}: {atk.AttackRange:0.#}\n{s}: {rate:0.##}";
        }

        string rr = LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, "game.tower.attack_range");
        return $"{rr}: {t.MagicRadius:0.#}";
    }

    // 코스트: '자원명 x수량' 줄 나열(자원명은 default 테이블 로컬라이즈, BuildingInfoUI 표기와 동일 계열).
    private string BuildCost(TowerAsset t)
    {
        if (t.Cost == null) return string.Empty;

        var sb = new StringBuilder();
        foreach (ResourceCost c in t.Cost)
        {
            if (c == null || c.Resource == null || c.Amount <= 0) continue;
            ResourceData data = ResolveResourceData(c.Resource);
            string rname = data != null
                ? LocalizationHelper.Get(LocalizationHelper.k_DefaultTable, data.NameKey)
                : c.Resource.ResourceID;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append($"{rname} x{c.Amount}");
        }
        return sb.ToString();
    }

    // TowerType별 공통 공격 스탯 해석(Tower.Attack과 동일 규칙). Magic/미할당 → null.
    private static TowerAsset.AttackFields AttackOf(TowerAsset t) => t.TowerType switch
    {
        TowerType.Single => t.Single?.Attack,
        TowerType.Area => t.Area?.Attack,
        TowerType.Chain => t.Chain?.Attack,
        _ => null,
    };

    // ResourceAsset.Data 채움(호출부 채움 규약, SystemMap §2) — BuildingInfoUI.ResolveData와 동일.
    private ResourceData ResolveResourceData(ResourceAsset resource)
    {
        if (resource == null) return null;
        if (resource.Data == null)
        {
            _resourceTable ??= DataTableManager.Get<ResourceTable>("ResourceTable");
            if (_resourceTable != null) resource.Data = _resourceTable.Get(resource.ResourceID);
        }
        return resource.Data;
    }
}