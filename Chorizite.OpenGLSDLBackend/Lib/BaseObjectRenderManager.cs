using Chorizite.Core.Render;
using DatReaderWriter.Enums;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Shared base for managers that handle instanced 3D object rendering.
    /// Encapsulates GPU buffer management and common instanced drawing logic.
    /// </summary>
    public abstract class BaseObjectRenderManager : IDisposable {
        protected readonly GL Gl;
        protected readonly OpenGLGraphicsDevice GraphicsDevice;
        protected readonly ObjectMeshManager MeshManager;

        // Render state tracking (Static so all managers sharing a context see the same state)
        public static uint CurrentVAO;
        public static uint CurrentIBO;
        public static uint CurrentAtlas;
        public static CullMode? CurrentCullMode;

        protected BaseObjectRenderManager(GL gl, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager) {
            Gl = gl;
            GraphicsDevice = graphicsDevice;
            MeshManager = meshManager;
            GLHelpers.CheckErrors();
        }

        protected unsafe void RenderObjectBatches(IShader shader, ObjectRenderData renderData,
            List<InstanceData> instanceTransforms, bool showCulling = true) {
            if (renderData.Batches.Count == 0 || instanceTransforms.Count == 0) return;

            // Bind the instance VBO and upload per-instance data
            GraphicsDevice.EnsureInstanceBufferCapacity(instanceTransforms.Count, sizeof(InstanceData));
            Gl.BindBuffer(GLEnum.ArrayBuffer, GraphicsDevice.InstanceVBO);

            // Upload instance data: mat4 transform, uint CellId
            var transformsSpan = CollectionsMarshal.AsSpan(instanceTransforms);
            fixed (InstanceData* ptr = transformsSpan) {
                Gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(instanceTransforms.Count * sizeof(InstanceData)), ptr);
            }

            RenderObjectBatches(shader, renderData, instanceTransforms.Count, 0, showCulling);
        }

        protected unsafe void RenderObjectBatches(IShader shader, ObjectRenderData renderData,
            int instanceCount, int instanceOffset, bool showCulling = true) {
            if (renderData.Batches.Count == 0 || instanceCount == 0) return;

            if (CurrentVAO != renderData.VAO) {
                Gl.BindVertexArray(renderData.VAO);
                CurrentVAO = renderData.VAO;
            }

            // Update instance attribute offsets in the VAO for this specific draw call
            // Locations are 3-6 (mat4) and 8 (uint CellId)
            Gl.BindBuffer(GLEnum.ArrayBuffer, GraphicsDevice.InstanceVBO);
            var stride = (uint)sizeof(InstanceData);
            var offset = (byte*)0 + (instanceOffset * sizeof(InstanceData));

            for (uint i = 0; i < 4; i++) {
                var loc = 3 + i;
                Gl.EnableVertexAttribArray(loc);
                Gl.VertexAttribPointer(loc, 4, GLEnum.Float, false, stride, (void*)(offset + (i * 16)));
                Gl.VertexAttribDivisor(loc, 1);
            }
            Gl.EnableVertexAttribArray(8);
            Gl.VertexAttribIPointer(8, 1, GLEnum.UnsignedInt, stride, (void*)(offset + 64));
            Gl.VertexAttribDivisor(8, 1);

            foreach (var batch in renderData.Batches) {
                var cullMode = showCulling ? batch.CullMode : CullMode.None;
                if (CurrentCullMode != cullMode) {
                    SetCullMode(cullMode);
                    CurrentCullMode = cullMode;
                }

                // Set texture index as a vertex attribute constant (location 7)
                Gl.DisableVertexAttribArray(7);
                Gl.VertexAttrib1(7, (float)batch.TextureIndex);

                // Bind texture array
                if (CurrentAtlas != (uint)batch.Atlas.TextureArray.NativePtr) {
                    batch.Atlas.TextureArray.Bind(0);
                    shader.SetUniform("uTextureArray", 0);
                    CurrentAtlas = (uint)batch.Atlas.TextureArray.NativePtr;
                }

                // Draw instanced
                if (CurrentIBO != batch.IBO) {
                    Gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);
                    CurrentIBO = batch.IBO;
                }

                Gl.DrawElementsInstanced(PrimitiveType.Triangles, (uint)batch.IndexCount,
                    DrawElementsType.UnsignedShort, (void*)0, (uint)instanceCount);
            }
        }

        protected void SetCullMode(CullMode mode) {
            switch (mode) {
                case CullMode.None:
                    Gl.Disable(EnableCap.CullFace);
                    break;
                case CullMode.Clockwise:
                    Gl.Enable(EnableCap.CullFace);
                    Gl.CullFace(GLEnum.Front);
                    break;
                case CullMode.CounterClockwise:
                case CullMode.Landblock:
                    Gl.Enable(EnableCap.CullFace);
                    Gl.CullFace(GLEnum.Back);
                    break;
            }
        }

        protected void IncrementInstanceRefCounts(List<SceneryInstance> instances) {
            var uniqueObjectIds = new HashSet<ulong>();
            foreach (var instance in instances) {
                uniqueObjectIds.Add(instance.ObjectId);
            }

            foreach (var objectId in uniqueObjectIds) {
                MeshManager.IncrementRefCount(objectId);
            }
        }

        protected void DecrementInstanceRefCounts(List<SceneryInstance> instances) {
            var uniqueObjectIds = new HashSet<ulong>();
            foreach (var instance in instances) {
                uniqueObjectIds.Add(instance.ObjectId);
            }

            foreach (var objectId in uniqueObjectIds) {
                MeshManager.DecrementRefCount(objectId);
            }
        }

        public virtual void Dispose() {
        }
    }
}