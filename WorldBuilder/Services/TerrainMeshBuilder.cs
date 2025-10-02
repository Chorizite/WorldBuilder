using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using System;

namespace WorldBuilder.Services {
    /// <summary>
    /// Converts TerrainMeshData to GPU resources
    /// </summary>
    public class TerrainMeshBuilder {
        private readonly IRenderer _renderer;

        public TerrainMeshBuilder(IRenderer renderer) {
            _renderer = renderer;
        }

        // Create GPU resources from mesh data
        public TerrainGpuMesh BuildGpuMesh(TerrainMeshData meshData) {
            return new TerrainGpuMesh(_renderer, meshData);
        }

        // Update existing GPU resources
        public void UpdateGpuMeshRegion(TerrainGpuMesh gpuMesh, TerrainMeshData meshData, int vertexOffset, int indexOffset) {
            gpuMesh.UpdateMeshRegion(meshData, vertexOffset, indexOffset);
        }
    }

    /// <summary>
    /// GPU-side mesh representation
    /// </summary>
    public class TerrainGpuMesh : IDisposable {
        public IVertexBuffer VertexBuffer { get; set; }
        public IIndexBuffer IndexBuffer { get; set; }
        public IVertexArray VertexArray { get; set; }

        public int VertexCount { get; set; }
        public int IndexCount { get; set; }

        public TerrainGpuMesh(IRenderer renderer, TerrainMeshData meshData) {
            VertexBuffer = renderer.GraphicsDevice.CreateVertexBuffer(VertexLandscape.Size * meshData.VertexCount, BufferUsage.Dynamic);
            VertexBuffer.SetData(meshData.Vertices.AsSpan(0, meshData.VertexCount));

            IndexBuffer = renderer.GraphicsDevice.CreateIndexBuffer(4 * meshData.IndexCount, BufferUsage.Dynamic);
            IndexBuffer.SetData(meshData.Indices.AsSpan(0, meshData.IndexCount));

            VertexArray = renderer.GraphicsDevice.CreateArrayBuffer(VertexBuffer, VertexLandscape.Format);

            VertexCount = meshData.VertexCount;
            IndexCount = meshData.IndexCount;
        }

        public void UpdateMeshRegion(TerrainMeshData meshData, int vertexOffset, int indexOffset) {
            VertexBuffer.SetSubData(meshData.Vertices.AsSpan(vertexOffset), vertexOffset * VertexLandscape.Size);
            IndexBuffer.SetSubData(meshData.Indices.AsSpan(indexOffset), indexOffset * 4, (meshData.IndexCount - indexOffset) * 4);
        }

        public void Dispose() {
            VertexArray.Dispose();
            IndexBuffer.Dispose();
            VertexBuffer.Dispose();
        }
    }
}