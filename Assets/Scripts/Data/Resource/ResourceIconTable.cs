using UnityEngine;

/// <summary>
/// <see cref="ResourceKind"/> → 자원 아이콘 매핑. 자원 종류만 알고 <see cref="ResourceAsset"/>를 모르는 뷰
/// (ProductionLineView 등)가 아이콘을 얻는 통로다.<br/>
/// 스프라이트는 여기가 아니라 각 <see cref="ResourceAsset"/>의 Icon에 authoring한다 — 이 테이블은 참조만 모은다(이중 authoring 방지).<br/>
/// 자원은 4종 고정이라(#337) 딕셔너리 대신 명시 필드를 둔다. 종류가 늘면 필드와 Get을 함께 추가할 것.
/// </summary>
[CreateAssetMenu(fileName = "ResourceIconTable", menuName = "Scriptable Objects/ResourceIconTable")]
public class ResourceIconTable : ScriptableObject
{
    [SerializeField] ResourceAsset _wood;
    [SerializeField] ResourceAsset _iron;
    [SerializeField] ResourceAsset _food;
    [SerializeField] ResourceAsset _mana;

    /// <summary>해당 자원의 아이콘. 미등록이거나 Icon 미할당이면 null(호출부가 이미지를 숨긴다).</summary>
    public Sprite Get(ResourceKind kind)
    {
        ResourceAsset asset = kind switch
        {
            ResourceKind.Wood => _wood,
            ResourceKind.Iron => _iron,
            ResourceKind.Food => _food,
            ResourceKind.Mana => _mana,
            _ => null,
        };
        return asset != null ? asset.Icon : null;
    }
}
