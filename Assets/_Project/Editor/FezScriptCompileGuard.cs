#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 降低外部改腳本時 CS2012（Assembly-CSharp.dll 被鎖）的機率，並提供一鍵解鎖。
/// </summary>
[InitializeOnLoad]
public static class FezScriptCompileGuard
{
    private const string PrefDelayRefresh = "Lichronicle.DelayScriptAutoRefresh";
    private const string PrefHintShown = "Lichronicle.DelayScriptAutoRefresh.HintShown";

    static FezScriptCompileGuard()
    {
        // 預設開啟：改腳本後不要立刻自動重編，改按 Ctrl+R。
        if (!EditorPrefs.HasKey(PrefDelayRefresh))
            EditorPrefs.SetBool(PrefDelayRefresh, true);

        EditorApplication.delayCall += ApplyRefreshMode;
        EditorApplication.delayCall += MaybeShowHint;
    }

    [MenuItem("Lichronicle/Tools/延後腳本自動編譯（避免 CS2012）/目前狀態", false, 10)]
    private static void StatusMenu()
    {
        var on = IsDelayRefreshOn();
        EditorUtility.DisplayDialog(
            "延後腳本自動編譯",
            on
                ? "目前：開啟\n\n外部（Cursor）改完腳本後，到 Unity 按 Ctrl+R（或 Assets → Refresh）才會編譯。\n可減少 Assembly-CSharp.dll 被鎖的 CS2012。"
                : "目前：關閉\n\nUnity 會一偵測到腳本變動就自動編譯（較容易撞上 CS2012）。",
            "OK");
    }

    [MenuItem("Lichronicle/Tools/延後腳本自動編譯（避免 CS2012）/開啟", false, 11)]
    private static void EnableDelayRefresh()
    {
        EditorPrefs.SetBool(PrefDelayRefresh, true);
        ApplyRefreshMode();
        Debug.Log("[Lichronicle] 已開啟延後腳本自動編譯。改完腳本後請按 Ctrl+R。");
    }

    [MenuItem("Lichronicle/Tools/延後腳本自動編譯（避免 CS2012）/開啟", true)]
    private static bool EnableDelayRefreshValidate() => !IsDelayRefreshOn();

    [MenuItem("Lichronicle/Tools/延後腳本自動編譯（避免 CS2012）/關閉", false, 12)]
    private static void DisableDelayRefresh()
    {
        EditorPrefs.SetBool(PrefDelayRefresh, false);
        ApplyRefreshMode();
        Debug.Log("[Lichronicle] 已關閉延後腳本自動編譯。Unity 會自動重編腳本。");
    }

    [MenuItem("Lichronicle/Tools/延後腳本自動編譯（避免 CS2012）/關閉", true)]
    private static bool DisableDelayRefreshValidate() => IsDelayRefreshOn();

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
        AssetDatabase.AllowAutoRefresh();
        AssetDatabase.Refresh();
        if (IsDelayRefreshOn())
            AssetDatabase.DisallowAutoRefresh();
        Debug.Log("[Lichronicle] 已手動 Refresh 腳本。延後模式開啟時也可直接按 Ctrl+R。");
    }

    private static bool IsDelayRefreshOn() => EditorPrefs.GetBool(PrefDelayRefresh, true);

    private static void ApplyRefreshMode()
    {
        if (IsDelayRefreshOn())
        {
            AssetDatabase.DisallowAutoRefresh();
            EditorPrefs.SetBool("kAutoRefresh", false);
        }
        else
        {
            AssetDatabase.AllowAutoRefresh();
            EditorPrefs.SetBool("kAutoRefresh", true);
        }

        Menu.SetChecked("Lichronicle/Tools/延後腳本自動編譯（避免 CS2012）/開啟", IsDelayRefreshOn());
        Menu.SetChecked("Lichronicle/Tools/延後腳本自動編譯（避免 CS2012）/關閉", !IsDelayRefreshOn());
    }

    private static void MaybeShowHint()
    {
        if (!IsDelayRefreshOn() || EditorPrefs.GetBool(PrefHintShown, false))
            return;

        EditorPrefs.SetBool(PrefHintShown, true);
        Debug.Log(
            "[Lichronicle] 已預設開啟「延後腳本自動編譯」，降低 CS2012 機率。" +
            "外部改完腳本後請按 Ctrl+R，或選單 Lichronicle → Tools → 立即重新編譯腳本。" +
            "若仍出現 CS2012：Lichronicle → Tools → 解除腳本編譯鎖。");
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
