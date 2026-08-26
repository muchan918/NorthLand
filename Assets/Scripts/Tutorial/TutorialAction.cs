using System;

[Flags]
public enum TutorialAction
{
    None = 0,
    SelectResident = 1 << 0,
    DragResident = 1 << 1,
    SelectTower = 1 << 2,
    PlaceTower = 1 << 3,
    MoveCamera = 1 << 4,
    UseBuildingShortcut = 1 << 5
}
