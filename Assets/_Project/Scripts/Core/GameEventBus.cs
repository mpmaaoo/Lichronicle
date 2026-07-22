using System;
using System.Collections.Generic;

namespace Lichronicle.Core
{
    /// <summary>
    /// 簡單的事件匯流排：讓系統之間用訊息溝通，避免互相直接引用。
    /// </summary>
    public sealed class GameEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();

        public void Subscribe<T>(Action<T> handler)
        {
            var t = typeof(T);
            if (!_handlers.TryGetValue(t, out var list))
            {
                list = new List<Delegate>();
                _handlers[t] = list;
            }

            if (!list.Contains(handler))
                list.Add(handler);
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            var t = typeof(T);
            if (!_handlers.TryGetValue(t, out var list))
                return;

            list.Remove(handler);
            if (list.Count == 0)
                _handlers.Remove(t);
        }

        public void Publish<T>(T evt)
        {
            var t = typeof(T);
            if (!_handlers.TryGetValue(t, out var list))
                return;

            // 避免在 handler 內增減訂閱造成迭代例外
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is Action<T> action)
                    action.Invoke(evt);
            }
        }
    }
}

