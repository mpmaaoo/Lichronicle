// TEMP-DEV-DELETE: 僅供開發重載場景；上線或穩定後請刪除此檔，並從場景移除掛載此元件的物件。
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 【臨時／開發用】按 <b>P</b> 重載目前作用中的場景。日後刪除本類別與場景內對應元件即可。
/// </summary>
[DisallowMultipleComponent]
public class TemporarySceneReloadHotkey : MonoBehaviour
{
    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.P))
            return;

        var active = SceneManager.GetActiveScene();
        if (!active.IsValid())
            return;

        // buildIndex 為 -1 表示該場景未加入 Build Settings，改以名稱載入（仍須在列表內）。
        if (active.buildIndex >= 0)
            SceneManager.LoadScene(active.buildIndex);
        else if (!string.IsNullOrEmpty(active.name))
            SceneManager.LoadScene(active.name);
    }
}
