using UnityEngine;

[CreateAssetMenu(fileName = "ResourceAsset", menuName = "Scriptable Objects/ResourceAsset")]
public class ResourceAsset : ScriptableObject
{
    public string ResourceID;

    // 자원 표기용 아이콘. 자원은 UI 어디서든 이름 텍스트 대신 이 아이콘으로 그린다
    // (BuildingInfoPanel의 비용/생산 Row가 첫 적용지) — 표기가 한 곳에서만 바뀌도록 SO에 둔다.
    public Sprite Icon;

    [HideInInspector]
    public ResourceData Data;
}
