/// <summary>
/// 格子方塊種類。Platform＝可踩實體；Occluder＝遮片牆；BoundaryWall＝視角可見才硬擋。
/// </summary>
public enum FezGridBlockKind
{
    Platform = 0,
    Occluder = 1,
    BoundaryWall = 2,
}
