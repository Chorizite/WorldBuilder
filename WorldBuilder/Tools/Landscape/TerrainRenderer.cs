using Chorizite.Core.Render;
using Chorizite.Core.Render.Enums;
using Chorizite.Core.Render.Vertex;
using Chorizite.OpenGLSDLBackend;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using WorldBuilder.Lib;
using WorldBuilder.Tools.Landscape;
using PrimitiveType = Chorizite.Core.Render.Enums.PrimitiveType;

public unsafe class TerrainRenderer : IDisposable {
    public ITextureArray TerrainAtlas { get; }
    public ITextureArray AlphaAtlas { get; }

    private readonly IRenderer _render;
    private GL gl;

    // Sphere rendering resources
    private uint _sphereVAO;
    private uint _sphereVBO;
    private uint _sphereIBO;
    private uint _sphereInstanceVBO;
    private int _sphereIndexCount;

    private bool disposed = false;
    internal bool wireframe;
    private IShader _terrainShader;

    public float AmbientLightIntensity = 0.45f;
    private IShader _sphereShader;

    // Grid properties
    public bool ShowGrid { get; set; } = true;

    public Vector3 LandblockGridColor { get; set; } = new Vector3(1.0f, 0f, 1.0f);
    public Vector3 CellGridColor { get; set; } = new Vector3(0f, 1f, 1f);
    public float GridLineWidth { get; set; } = 1f;
    public float GridOpacity { get; set; } = .40f;

    // Sphere properties
    public Vector3 SphereColor { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);
    public float SphereRadius { get; set; } = 4.6f;
    public float SphereHeightOffset { get; set; } = 0.0f;
    public Vector3 LightDirection { get; set; } = new Vector3(0.5f, 0.3f, -0.3f);
    public float SpecularPower { get; set; } = 32.0f;
    public Vector3 SphereGlowColor { get; set; } = new(0);
    public float SphereGlowIntensity { get; set; } = 1.0f;
    public float SphereGlowPower { get; set; } = 0.5f;

    public TerrainRenderer(IRenderer render, ITextureArray terrainAtlas, ITextureArray alphaAtlas) {
        TerrainAtlas = terrainAtlas;
        AlphaAtlas = alphaAtlas;
        _render = render;
        this.gl = (render as OpenGLRenderer).GraphicsDevice.GL;

        InitializeShaders();
        InitializeSphereGeometry();
    }

    private void InitializeShaders() {
        var a1 = typeof(OpenGLRenderer).Assembly;
        // load embedded resource
        

        _terrainShader = _render.GraphicsDevice.CreateShader("Landscape", GetEmbeddedResource("Shaders.Landscape.vert", a1), GetEmbeddedResource("Shaders.Landscape.frag", a1));
        _terrainShader.Bind();
        _sphereShader = _render.GraphicsDevice.CreateShader("Sphere", GetEmbeddedResource("Shaders.Sphere.vert"), GetEmbeddedResource("Shaders.Sphere.frag"));
    }

    private void InitializeSphereGeometry() {
        // Create a sphere mesh (icosphere or UV sphere)
        var vertices = CreateSphere(16, 12); // 16 longitude, 12 latitude segments
        var indices = CreateSphereIndices(16, 12);
        _sphereIndexCount = indices.Length;

        gl.GenVertexArrays(1, out _sphereVAO);
        gl.BindVertexArray(_sphereVAO);

        gl.GenBuffers(1, out _sphereVBO);
        gl.BindBuffer(GLEnum.ArrayBuffer, _sphereVBO);
        fixed (VertexPositionNormal* ptr = vertices) {
            gl.BufferData(GLEnum.ArrayBuffer, (nuint)(vertices.Length * VertexPositionNormal.Size), ptr, GLEnum.StaticDraw);
        }

        int stride = VertexPositionNormal.Size;
        // Position (location 0)
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, (uint)stride, (void*)0);
        // Normal (location 1)
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, GLEnum.Float, false, (uint)stride, (void*)(3 * sizeof(float)));

        // Instance buffer (initially empty, dynamic)
        gl.GenBuffers(1, out _sphereInstanceVBO);
        gl.BindBuffer(GLEnum.ArrayBuffer, _sphereInstanceVBO);
        gl.BufferData(GLEnum.ArrayBuffer, 0, null, GLEnum.DynamicDraw);
        // Instance position (location 2)
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 3, GLEnum.Float, false, (uint)sizeof(Vector3), null);
        gl.VertexAttribDivisor(2, 1);

        gl.GenBuffers(1, out _sphereIBO);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, _sphereIBO);
        fixed (uint* iptr = indices) {
            gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), iptr, GLEnum.StaticDraw);
        }

        gl.BindVertexArray(0);
    }

    private VertexPositionNormal[] CreateSphere(int longitudeSegments, int latitudeSegments) {
        var vertices = new List<VertexPositionNormal>();

        // Generate vertices
        for (int lat = 0; lat <= latitudeSegments; lat++) {
            float theta = lat * MathF.PI / latitudeSegments;
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int lon = 0; lon <= longitudeSegments; lon++) {
                float phi = lon * 2 * MathF.PI / longitudeSegments;
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);

                float x = cosPhi * sinTheta;
                float y = cosTheta;
                float z = sinPhi * sinTheta;

                Vector3 position = new Vector3(x, y, z);
                Vector3 normal = Vector3.Normalize(position);

                vertices.Add(new VertexPositionNormal(position, normal));
            }
        }

        return vertices.ToArray();
    }

    private uint[] CreateSphereIndices(int longitudeSegments, int latitudeSegments) {
        var indices = new List<uint>();

        for (int lat = 0; lat < latitudeSegments; lat++) {
            for (int lon = 0; lon < longitudeSegments; lon++) {
                uint current = (uint)(lat * (longitudeSegments + 1) + lon);
                uint next = current + (uint)(longitudeSegments + 1);

                // First triangle
                indices.Add(current);
                indices.Add(next);
                indices.Add(current + 1);

                // Second triangle
                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }

        return indices.ToArray();
    }

    internal static string GetEmbeddedResource(string filename, Assembly? ass = null) {
        var assembly = ass ?? typeof(TerrainRenderer).Assembly;
        var resourceName = "WorldBuilder." + filename;

        if (ass is not null) {
            resourceName = "Chorizite.OpenGLSDLBackend." + filename;
        }

        using (Stream stream = assembly.GetManifestResourceStream(resourceName))
        using (StreamReader reader = new StreamReader(stream)) {
            string result = reader.ReadToEnd();
            return result;
        }
    }

    public void RenderChunks(ICamera camera, float aspectRatio, IEnumerable<TerrainChunk> visibleChunks, TerrainEditingContext editingContext, float width, float height) {
        var center = 254 * 192f / 2f;

        // Clear the screen first
        gl.ClearColor(0.2f, 0.3f, 0.8f, 1.0f); // Sky blue background
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Calculate matrices once
        Matrix4x4 model = Matrix4x4.Identity;
        Matrix4x4 view = camera.GetViewMatrix();
        Matrix4x4 projection = camera.GetProjectionMatrix(aspectRatio, 1.0f, center * 4f);
        Matrix4x4 viewProjection = view * projection;

        // Estimate camera distance to terrain (average height or a reference plane)
        // For simplicity, assume terrain is near z=0
        float cameraDistance = MathF.Abs(camera.Position.Z);
        if (camera is OrthographicTopDownCamera orthoCamera)
        {
            // For orthographic, use orthographic size as a proxy for distance
            cameraDistance = orthoCamera.OrthographicSize;
        }

        // Render terrain chunks
        RenderTerrain(visibleChunks, model, viewProjection, cameraDistance, width, height);

        // Render active vertex spheres
        if (editingContext.ActiveVertices.Count > 0)
        {
            RenderActiveSpheres(editingContext, camera, model, viewProjection);
        }

    }

    private void RenderTerrain(IEnumerable<TerrainChunk> visibleChunks, Matrix4x4 model, Matrix4x4 viewProjection, float cameraDistance, float width, float height) {
        _terrainShader.Bind();

        _terrainShader.SetUniform("xAmbient", AmbientLightIntensity);
        _terrainShader.SetUniform("xWorld", model);
        _terrainShader.SetUniform("xViewProjection", viewProjection);
        _terrainShader.SetUniform("uAlpha", 1f);

        // Set grid uniforms
        _terrainShader.SetUniform("uShowLandblockGrid", ShowGrid ? 1 : 0);
        _terrainShader.SetUniform("uShowCellGrid", ShowGrid ? 1 : 0);
        _terrainShader.SetUniform("uLandblockGridColor", LandblockGridColor);
        _terrainShader.SetUniform("uCellGridColor", CellGridColor);
        _terrainShader.SetUniform("uGridLineWidth", GridLineWidth);
        _terrainShader.SetUniform("uGridOpacity", GridOpacity);
        _terrainShader.SetUniform("uCameraDistance", cameraDistance);
        _terrainShader.SetUniform("uScreenHeight", height);

        // Bind texture atlases
        TerrainAtlas.Bind(0);
        _terrainShader.SetUniform("xOverlays", 0);
        AlphaAtlas.Bind(1);
        _terrainShader.SetUniform("xAlphas", 1);
        
        foreach (var chunk in visibleChunks) {
            if (chunk.VertexArray == null || chunk.IndexBuffer == null) {
                continue; // Skip chunks without graphics resources
            }

            // Bind chunk's vertex array
            chunk.VertexArray.Bind();
            chunk.VertexBuffer.Bind();
            chunk.IndexBuffer.Bind();

            // Draw the chunk
            _render.GraphicsDevice.DrawElements(PrimitiveType.TriangleList, chunk.IndexCount);

            // Unbind for next chunk
            chunk.VertexArray.Unbind();
            chunk.VertexBuffer.Unbind();
            chunk.IndexBuffer.Unbind();
        }
    }

    private void RenderActiveSpheres(TerrainEditingContext editingContext, ICamera camera, Matrix4x4 model, Matrix4x4 viewProjection) {
        if (editingContext.ActiveVertices.Count == 0) return;

        int count = editingContext.ActiveVertices.Count;
        var positions = new Vector3[count];
        for (int i = 0; i < count; i++) {
            try
            {
                var vertex = editingContext.ActiveVertices[i];
                positions[i] = new Vector3(vertex.X, vertex.Y, editingContext.TerrainProvider.GetHeightAtPosition(vertex.X, vertex.Y) + SphereHeightOffset);
            }
            catch
            {
                positions[i] = Vector3.Zero;
            }
        }

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);


        _sphereShader.Bind();
        _sphereShader.SetUniform("uViewProjection", viewProjection);
        _sphereShader.SetUniform("uCameraPosition", camera.Position);
        _sphereShader.SetUniform("uSphereColor", SphereColor);
        Vector3 normLight = Vector3.Normalize(LightDirection);
        _sphereShader.SetUniform("uLightDirection", normLight);
        _sphereShader.SetUniform("uAmbientIntensity", AmbientLightIntensity);
        _sphereShader.SetUniform("uSpecularPower", SpecularPower);
        _sphereShader.SetUniform("uGlowColor", SphereGlowColor);
        _sphereShader.SetUniform("uGlowIntensity", SphereGlowIntensity);
        _sphereShader.SetUniform("uGlowPower", SphereGlowPower);
        _sphereShader.SetUniform("uSphereRadius", SphereRadius);

        // Update instance buffer
        gl.BindBuffer(GLEnum.ArrayBuffer, _sphereInstanceVBO);
        fixed (Vector3* posPtr = positions) {
            gl.BufferData(GLEnum.ArrayBuffer, (nuint)(count * sizeof(Vector3)), posPtr, GLEnum.DynamicDraw);
        }

        // Bind VAO and draw instanced
        gl.BindVertexArray(_sphereVAO);
        gl.DrawElementsInstanced(GLEnum.Triangles, (uint)_sphereIndexCount, GLEnum.UnsignedInt, null, (uint)count);

        gl.BindVertexArray(0);
        gl.UseProgram(0);

        // Disable blending
        gl.Disable(EnableCap.Blend);
    }

    public void Dispose() {
        if (!disposed) {
            gl.DeleteBuffer(_sphereVBO);
            gl.DeleteBuffer(_sphereIBO);
            gl.DeleteBuffer(_sphereInstanceVBO);
            gl.DeleteVertexArray(_sphereVAO);

            disposed = true;
        }
    }
}