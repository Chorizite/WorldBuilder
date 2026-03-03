using Chorizite.Core.Render;
using System;
using System.Numerics;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Common interface for all render managers to enforce a consistent pattern for their lifecycle.
    /// </summary>
    public interface IRenderManager : IDisposable {
        /// <summary>
        /// Whether this manager has new data that requires a re-preparation of render batches.
        /// </summary>
        bool NeedsPrepare { get; }

        /// <summary>
        /// Number of pending GPU uploads.
        /// </summary>
        int QueuedUploads { get; }

        /// <summary>
        /// Number of pending background generations.
        /// </summary>
        int QueuedGenerations { get; }

        /// <summary>
        /// Initialize the manager with its main shader.
        /// </summary>
        /// <param name="shader">The shader to use.</param>
        void Initialize(IShader shader);

        /// <summary>
        /// Update the manager's state.
        /// </summary>
        /// <param name="deltaTime">Time since last update.</param>
        /// <param name="camera">Current camera.</param>
        void Update(float deltaTime, ICamera camera);

        /// <summary>
        /// Process pending GPU uploads.
        /// </summary>
        /// <param name="timeBudgetMs">Time budget in milliseconds.</param>
        /// <returns>Time spent processing uploads.</returns>
        float ProcessUploads(float timeBudgetMs);

        /// <summary>
        /// Prepare render batches based on the current camera view.
        /// </summary>
        /// <param name="viewProjectionMatrix">The view-projection matrix.</param>
        /// <param name="cameraPosition">The camera position.</param>
        void PrepareRenderBatches(Matrix4x4 viewProjectionMatrix, Vector3 cameraPosition);

        /// <summary>
        /// Render the prepared batches for a specific pass.
        /// </summary>
        /// <param name="renderPass">The render pass.</param>
        void Render(RenderPass renderPass);

        /// <summary>
        /// Invalidate a landblock, forcing it to be regenerated.
        /// </summary>
        /// <param name="lbX">Landblock X coordinate.</param>
        /// <param name="lbY">Landblock Y coordinate.</param>
        void InvalidateLandblock(int lbX, int lbY);
    }
}
