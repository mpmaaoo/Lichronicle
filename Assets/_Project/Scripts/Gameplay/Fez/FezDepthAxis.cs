using UnityEngine;

public enum FezDepthAxisSource
{
    /// <summary>四向視角狀態（推薦）：依樞紐 Y 角對應 ±X / ±Z，不依賴鏡頭。</summary>
    WorldViewState = 0,
    MainCamera = 1,
    ExplicitTransform = 2
}

public static class FezDepthAxis
{
    public static Vector3 GetViewForward(FezDepthAxisSource source, Transform explicitTransform, Camera explicitCamera)
    {
        if (source == FezDepthAxisSource.WorldViewState)
        {
            if (FezWorldViewState.Active != null)
                return FezWorldViewState.Active.DepthAxisWorld;
            return Vector3.forward;
        }

        Vector3 f;

        if (source == FezDepthAxisSource.ExplicitTransform && explicitTransform != null)
        {
            f = explicitTransform.forward;
        }
        else
        {
            var cam = explicitCamera != null ? explicitCamera : Camera.main;
            f = cam != null ? cam.transform.forward : Vector3.forward;
        }

        f.y = 0f;
        if (f.sqrMagnitude < 0.0001f)
            return Vector3.forward;

        return f.normalized;
    }

    public static float DepthOf(Vector3 worldPos, Vector3 viewForward)
    {
        return Vector3.Dot(worldPos, viewForward);
    }
}

