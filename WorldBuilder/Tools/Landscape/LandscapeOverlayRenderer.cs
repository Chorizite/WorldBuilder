using Chorizite.Common.Enums;
using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using DatReaderWriter.DBObjs;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Tools.Landscape;

internal class LandscapeOverlayRenderer : IDisposable {
    private const int maxVerts = 81 * 32 * 32;
    private const int maxIndices = 120 * 32 * 32;

    private TerrainProvider terrainProvider;
    private IRenderer render;
    private VertexLandscape[] vertices = new VertexLandscape[maxVerts];
    private uint[] indices = new uint[maxIndices];
    private int _numVerts;
    private int _numIndices;

    public IVertexBuffer VertexBuffer { get; set; }
    public IIndexBuffer IndexBuffer { get; set; }
    public IVertexArray VertexArray { get; set; }

    public LandscapeOverlayRenderer(TerrainProvider terrainProvider, IRenderer render) {
        this.terrainProvider = terrainProvider;
        this.render = render;

        VertexBuffer = render.GraphicsDevice.CreateVertexBuffer(VertexLandscape.Size * maxVerts, BufferUsage.Dynamic);
        IndexBuffer = render.GraphicsDevice.CreateIndexBuffer(maxIndices, BufferUsage.Dynamic);
        VertexArray = render.GraphicsDevice.CreateArrayBuffer(VertexBuffer, VertexLandscape.Format);
    }

    public void UpdateOverlay(Dictionary<ushort, TerrainEntry[]> previewLandblocks, TerrainEditingContext editingContext) {
        uint currentVertexIndex = 0;
        uint currentIndexPosition = 0;
        var verticesSpan = vertices.AsSpan();
        var indicesSpan = indices.AsSpan();

        foreach (var (landblockId, terrain) in previewLandblocks) {
            var landblockX = (landblockId >> 8) & 0xFF;
            var landblockY = landblockId & 0xFF;
            float baseLandblockX = landblockX * 192f;
            float baseLandblockY = landblockY * 192f;

            // Generate all cells for this landblock
            for (uint cellY = 0; cellY < 8; cellY++) {
                for (uint cellX = 0; cellX < 8; cellX++) {
                    editingContext.TerrainProvider.GenerateCell(
                        baseLandblockX, baseLandblockY, cellX, cellY,
                        terrain, landblockId,
                        ref currentVertexIndex, ref currentIndexPosition,
                        verticesSpan, indicesSpan
                    );
                }
            }
        }

        _numVerts = (int)currentVertexIndex;
        _numIndices = (int)currentIndexPosition;

        // adjust z
        for (var i = 0; i < _numVerts; i++) {
            verticesSpan[i].Position.Z += 0.05f;
        }

        if (_numVerts > 0) {
            VertexBuffer.SetData(vertices.AsSpan(0, _numVerts));
        }
        if (_numIndices > 0) {
            IndexBuffer.SetData(indices.AsSpan(0, _numIndices));
        }
    }

    public void RenderOverlays(IShader _terrainShader, Matrix4x4 model, Matrix4x4 viewProjection, ITextureArray terrainAtlas, ITextureArray alphaAtlas) {
        if (_numVerts == 0 || _numIndices == 0) return;
        _terrainShader.Bind();


        // Bind texture atlases
        terrainAtlas.Bind(0);
        _terrainShader.SetUniform("xOverlays", 0);
        alphaAtlas.Bind(1);
        _terrainShader.SetUniform("xAlphas", 1);
        _terrainShader.SetUniform("uAlpha", 1f);

        VertexArray.Bind();
        VertexBuffer.Bind();
        IndexBuffer.Bind();

        render.GraphicsDevice.DrawElements(PrimitiveType.TriangleList, _numIndices);

        VertexArray.Unbind();
        VertexBuffer.Unbind();
        IndexBuffer.Unbind();

        _terrainShader.Unbind();
    }

    public void Dispose() {
        VertexBuffer?.Dispose();
        IndexBuffer?.Dispose();
        VertexArray?.Dispose();
    }
}