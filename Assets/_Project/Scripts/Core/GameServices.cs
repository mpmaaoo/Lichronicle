using UnityEngine;

namespace Lichronicle.Core
{
    /// <summary>
    /// 目前先用最輕量的 Service 容器，讓 MonoBehaviour 好取得共用服務。
    /// 之後若需要可替換成更完整的 DI。
    /// </summary>
    public static class GameServices
    {
        public static GameEventBus Events { get; } = new GameEventBus();

        public static void ResetForPlayMode()
        {
            // 目前沒有可重置狀態；保留擴充點（例如存檔/狀態機/全域設定）。
        }
    }
}

