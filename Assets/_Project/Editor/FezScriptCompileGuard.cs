#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 維持 Unity 自動重新整理（不必手動 Ctrl+R），並提供一鍵解除腳本編譯鎖。
/// 不呼叫 DisallowAutoRefresh／AllowAutoRefresh，避免 m_DisallowAutoRefresh 断言。
/// </summary>
[InitializeOnLoad]
public static class FezScriptCompileGuard
{
    private const string PrefDelayRefresh = "Lichronicle.DelayScriptAutoRefresh";

    static FezScriptCompileGuard()
    {
        // 清掉舊版「延後編譯」偏好，並確保 Auto Refresh 開啟。
        if (EditorPrefs.HasKey(PrefDelayRefresh))
            EditorPrefs.DeleteKey(PrefDelayRefresh);

        EditorApplication.delayCall += EnsureAutoRefreshOn;
    }

    [MenuItem("Lichronicle/Tools/開啟自動重新整理（不必 Ctrl+R）", false, 10)]
    private static void EnableAutoRefreshMenu()
    {
        EnsureAutoRefreshOn();
        Debug.Log("[Lichronicle] 已開啟 Unity 自動重新整理。改腳本後會自動編譯，不必按 Ctrl+R。");
        EditorUtility.DisplayDialog(
            "自動重新整理",
            "已開啟。\n\n外部改完腳本後，Unity 會自動偵測並編譯，不必再按 Ctrl+R。",
            "OK");
    }

    [MenuItem("Lichronicle/Tools/解除腳本編譯鎖 (CS2012)", false, 30)]
    private static void UnlockCompileLock()
    {
        var killed = KillIlppRunners();
        var removed = DeleteLockedAssemblies();
        AssetDatabase.Refresh();
        Debug.Log($"[Lichronicle] 已嘗試解除編譯鎖：結束 ILPP {killed} 個，刪除產物 {removed} 個，並 Refresh。");
        EditorUtility.DisplayDialog(
            "解除腳本編譯鎖",
            $"已結束 ILPP：{killed}\n已刪除編譯產物：{removed}\n\n若 Console 仍顯示 CS2012，請完全關閉 Unity 再重開。",
            "OK");
    }

    [MenuItem("Lichronicle/Tools/立即重新編譯腳本", false, 31)]
    private static void RefreshScriptsNow()
    {
        AssetDatabase.Refresh();
        Debug.Log("[Lichronicle] 已手動 Refresh 腳本。");
    }

    private static void EnsureAutoRefreshOn()
    {
        EditorPrefs.SetBool("kAutoRefresh", true);
    }

    private static int KillIlppRunners()
    {
        var count = 0;
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("Unity.ILPP.Runner");
        }
        catch
        {
            return 0;
        }

        for (var i = 0; i < processes.Length; i++)
        {
            var process = processes[i];
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                    count++;
                }
            }
            catch
            {
                // ignored
            }
            finally
            {
                try { process?.Dispose(); } catch { /* ignored */ }
            }
        }

        return count;
    }

    private static int DeleteLockedAssemblies()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
            return 0;

        var removed = 0;
        removed += DeleteGlob(Path.Combine(projectRoot, "Library", "Bee", "artifacts"), "Assembly-CSharp*");
        removed += DeleteGlob(Path.Combine(projectRoot, "Library", "ScriptAssemblies"), "Assembly-CSharp*");
        return removed;
    }

    private static int DeleteGlob(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
            return 0;

        var count = 0;
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, pattern, SearchOption.AllDirectories);
        }
        catch
        {
            return 0;
        }

        for (var i = 0; i < files.Length; i++)
        {
            try
            {
                File.Delete(files[i]);
                count++;
            }
            catch
            {
                // 仍被編輯器自己鎖住時會失敗，需重開 Unity。
            }
        }

        return count;
    }
}
#endif
