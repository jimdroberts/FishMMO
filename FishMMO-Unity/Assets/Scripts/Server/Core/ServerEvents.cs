using System;

namespace FishMMO.Server.Core
{
    /// <summary>
    /// A concrete implementation of <see cref="IServerEvents"/>.
    /// Implements the event pattern, allowing other components to subscribe to server initialization events.
    /// </summary>
    public class ServerEvents : IServerEvents
    {
        /// <summary>
        /// Event triggered when the login server is initialized.
        /// </summary>
        public Action OnLoginServerInitialized { get; set; }

        /// <summary>
        /// Event triggered when the world server is initialized.
        /// </summary>
        public Action OnWorldServerInitialized { get; set; }

        /// <summary>
        /// Event triggered when the scene server is initialized.
        /// </summary>
        public Action OnSceneServerInitialized { get; set; }
    }
}