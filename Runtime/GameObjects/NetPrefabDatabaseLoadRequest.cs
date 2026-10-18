using System;
using UnityEngine;

namespace Keel.RelayNet.GameObjects
{
    /// <summary>
    /// A hook that allows initiate the loading of the <see cref="NetPrefabDatabase"/> in the background.
    /// Could be used to integrate async loading of the <see cref="NetPrefabDatabase"/> with the game code.
    /// </summary>
    public sealed class NetPrefabDatabaseLoadRequest
    {
        /// <summary>
        /// Fired when the request is done.
        /// </summary>
        public event Action Completed = delegate { };

        /// <summary>
        /// Is true when the request is done.
        /// </summary>
        public bool IsDone =>
            _resourceRequest.isDone;

        /// <summary>
        /// Progress of the request.
        /// </summary>
        public float Progress =>
            _resourceRequest.progress;

        private readonly ResourceRequest _resourceRequest;

        private NetPrefabDatabaseLoadRequest(ResourceRequest resourceRequest)
        {
            _resourceRequest = resourceRequest;
            _resourceRequest.completed += OnCompleted;
        }

        private void OnCompleted(AsyncOperation asyncOp)
        {
            _resourceRequest.completed -= OnCompleted;
            Completed?.Invoke();
        }

        public static NetPrefabDatabaseLoadRequest Create()
        {
            var resourceRequest = NetPrefabDatabase.LoadAsync();
            return resourceRequest == null ? null : new NetPrefabDatabaseLoadRequest(resourceRequest);
        }
    }
}