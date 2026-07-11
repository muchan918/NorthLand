using UnityEngine;

[CreateAssetMenu(fileName = "ResourceAsset", menuName = "Scriptable Objects/ResourceAsset")]
public class ResourceAsset : ScriptableObject
{
    public string ResourceID;

    [HideInInspector]
    public ResourceData Data;
}
