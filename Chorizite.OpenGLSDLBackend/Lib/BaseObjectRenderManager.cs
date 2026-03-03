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
        protected readonly bool _useModernRendering;

        // Render state tracking (Static so all managers sharing a context see the same state)
        public static uint CurrentVAO;
        public static uint CurrentIBO;
        public static uint CurrentAtlas;
        public static CullMode? CurrentCullMode;

        // Global instance buffers for all landblocks managed by this manager
        private uint _worldInstanceBuffer;
        private uint _worldInstanceSSBO;
        private int _worldInstanceCapacity = 1024 * 16384; // 16M instances
        private readonly List<(int Offset, int Size)> _freeSlices = new();
        private readonly object _allocationLock = new();

        // Modern rendering MDI buffers
        private uint _mdiCommandBuffer;
        private int _mdiCommandCapacity = 1024 * 32;
        private uint _modernBatchBuffer;
        private uint _modernInstanceBuffer; // Scratch buffer for non-consolidated modern draws
        private int _modernInstanceCapacity = 1024 * 16;

        // Reusable arrays to avoid allocations per frame
        private DrawElementsIndirectCommand[] _commands = Array.Empty<DrawElementsIndirectCommand>();
        private ModernBatchData[] _modernBatches = Array.Empty<ModernBatchData>();
        private readonly List<LandblockMdiCommand>[] _cullGroups = [new(), new(), new(), new()];

        protected unsafe BaseObjectRenderManager(GL gl, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager, bool createWorldBuffer = true, int initialCapacity = 1024 * 16384) {
            Gl = gl;
            GraphicsDevice = graphicsDevice;
            MeshManager = meshManager;
            _useModernRendering = graphicsDevice.HasOpenGL43 && graphicsDevice.HasBindless;
            _worldInstanceCapacity = initialCapacity;

            if (createWorldBuffer) {
                // Initialize global instance buffer
                Gl.GenBuffers(1, out _worldInstanceBuffer);
                Gl.BindBuffer(GLEnum.ArrayBuffer, _worldInstanceBuffer);
                Gl.BufferData(GLEnum.ArrayBuffer, (nuint)(_worldInstanceCapacity * sizeof(InstanceData)), null, GLEnum.DynamicDraw);
                GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                GpuMemoryTracker.TrackAllocation(_worldInstanceCapacity * sizeof(InstanceData), GpuResourceType.Buffer);
                _freeSlices.Add((0, _worldInstanceCapacity));
            }

            if (_useModernRendering) {
                if (createWorldBuffer) {
                    Gl.GenBuffers(1, out _worldInstanceSSBO);
                    Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _worldInstanceSSBO);
                    Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_worldInstanceCapacity * sizeof(InstanceData)), null, GLEnum.DynamicDraw);
                    GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                    GpuMemoryTracker.TrackAllocation(_worldInstanceCapacity * sizeof(InstanceData), GpuResourceType.Buffer);
                }

                Gl.GenBuffers(1, out _mdiCommandBuffer);
                Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _mdiCommandBuffer);
                Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(_mdiCommandCapacity * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
                GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                GpuMemoryTracker.TrackAllocation(_mdiCommandCapacity * sizeof(DrawElementsIndirectCommand), GpuResourceType.Buffer);

                Gl.GenBuffers(1, out _modernBatchBuffer);
                Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernBatchBuffer);
                Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_mdiCommandCapacity * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
                GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                GpuMemoryTracker.TrackAllocation(_mdiCommandCapacity * sizeof(ModernBatchData), GpuResourceType.Buffer);

                Gl.GenBuffers(1, out _modernInstanceBuffer);
                Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernInstanceBuffer);
                Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_modernInstanceCapacity * sizeof(InstanceData)), null, GLEnum.DynamicDraw);
                GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                GpuMemoryTracker.TrackAllocation(_modernInstanceCapacity * sizeof(InstanceData), GpuResourceType.Buffer);
            }

            GLHelpers.CheckErrors(Gl);
            UpdateGpuStats();
        }

        public virtual unsafe void UpdateGpuStats() {
            var name = GetType().Name;
            if (_worldInstanceBuffer != 0) {
                int totalFree;
                lock (_allocationLock) {
                    totalFree = _freeSlices.Sum(s => s.Size);
                }
                GpuMemoryTracker.TrackNamedBuffer($"{name} World Instance VBO",
                    (long)_worldInstanceCapacity * sizeof(InstanceData),
                    (long)(_worldInstanceCapacity - totalFree) * sizeof(InstanceData));
            }

            if (_useModernRendering) {
                if (_worldInstanceSSBO != 0) {
                    int totalFree;
                    lock (_allocationLock) {
                        totalFree = _freeSlices.Sum(s => s.Size);
                    }
                    GpuMemoryTracker.TrackNamedBuffer($"{name} World Instance SSBO",
                        (long)_worldInstanceCapacity * sizeof(InstanceData),
                        (long)(_worldInstanceCapacity - totalFree) * sizeof(InstanceData));
                }

                GpuMemoryTracker.TrackNamedBuffer($"{name} MDI Command Buffer",
                    (long)_mdiCommandCapacity * sizeof(DrawElementsIndirectCommand),
                    0);

                GpuMemoryTracker.TrackNamedBuffer($"{name} Modern Batch Buffer",
                    (long)_mdiCommandCapacity * sizeof(ModernBatchData),
                    0);

                GpuMemoryTracker.TrackNamedBuffer($"{name} Modern Instance Buffer",
                    (long)_modernInstanceCapacity * sizeof(InstanceData),
                    0);
            }
        }

        protected int AllocateInstanceSlice(int count) {
            lock (_allocationLock) {
                for (int i = 0; i < _freeSlices.Count; i++) {
                    var slice = _freeSlices[i];
                    if (slice.Size >= count) {
                        _freeSlices.RemoveAt(i);
                        if (slice.Size > count) {
                            _freeSlices.Insert(i, (slice.Offset + count, slice.Size - count));
                        }
                        UpdateGpuStats();
                        return slice.Offset;
                    }
                }
            }
            return -1;
        }

        protected void FreeInstanceSlice(int offset, int count) {
            if (offset < 0 || count <= 0) return;

            lock (_allocationLock) {
                // Insert and maintain sorted order by offset to allow merging
                int insertIdx = 0;
                while (insertIdx < _freeSlices.Count && _freeSlices[insertIdx].Offset < offset) {
                    insertIdx++;
                }
                _freeSlices.Insert(insertIdx, (offset, count));

                // Merge with next slice if contiguous
                if (insertIdx + 1 < _freeSlices.Count) {
                    var next = _freeSlices[insertIdx + 1];
                    if (offset + count == next.Offset) {
                        _freeSlices[insertIdx] = (offset, count + next.Size);
                        _freeSlices.RemoveAt(insertIdx + 1);
                    }
                }

                // Merge with previous slice if contiguous
                if (insertIdx > 0) {
                    var prev = _freeSlices[insertIdx - 1];
                    var current = _freeSlices[insertIdx];
                    if (prev.Offset + prev.Size == current.Offset) {
                        _freeSlices[insertIdx - 1] = (prev.Offset, prev.Size + current.Size);
                        _freeSlices.RemoveAt(insertIdx);
                    }
                }
                UpdateGpuStats();
            }
        }

        protected unsafe void UploadInstanceData(int offset, List<InstanceData> data) {
            Gl.BindBuffer(GLEnum.ArrayBuffer, _worldInstanceBuffer);
            var span = CollectionsMarshal.AsSpan(data);
            fixed (InstanceData* ptr = span) {
                Gl.BufferSubData(GLEnum.ArrayBuffer, (nint)(offset * sizeof(InstanceData)), (nuint)(data.Count * sizeof(InstanceData)), ptr);
            }

            if (_useModernRendering) {
                Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _worldInstanceSSBO);
                fixed (InstanceData* ptr = span) {
                    Gl.BufferSubData(GLEnum.ShaderStorageBuffer, (nint)(offset * sizeof(InstanceData)), (nuint)(data.Count * sizeof(InstanceData)), ptr);
                }
            }
        }

        public unsafe void RenderConsolidated(IShader shader, List<ObjectLandblock> landblocks, int renderPass) {
            if (landblocks.Count == 0) return;

            shader.SetUniform("uFilterByCell", 0);

            for (int i = 0; i < 4; i++) _cullGroups[i].Clear();
            foreach (var lb in landblocks) {
                foreach (var kvp in lb.MdiCommands) {
                    var idx = (int)kvp.Key;
                    if (idx < 0 || idx >= 4) continue;
                    _cullGroups[idx].AddRange(kvp.Value);
                }
            }

            var stride = (uint)sizeof(InstanceData);
            for (int i = 0; i < 4; i++) {
                var group = _cullGroups[i];
                if (group.Count == 0) continue;

                // Sort to minimize state changes using pre-calculated sort key
                if (renderPass == 1) {
                    // Transparent pass: sort by BaseInstance (order in buffer / landblock order)
                    group.Sort((a, b) => a.Command.BaseInstance.CompareTo(b.Command.BaseInstance));
                }
                else {
                    // Opaque pass: sort front-to-back for state efficiency
                    group.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
                }

                var cullMode = (CullMode)i;
                if (CurrentCullMode != cullMode) {
                    SetCullMode(cullMode);
                    CurrentCullMode = cullMode;
                }

                uint? lastBaseInstance = null;
                uint? lastTextureIndex = null;

                foreach (var cmd in group) {
                    if (renderPass == 1 && !cmd.IsTransparent) continue;

                    bool vaoChanged = false;
                    if (CurrentVAO != cmd.VAO) {
                        Gl.BindVertexArray(cmd.VAO);
                        CurrentVAO = cmd.VAO;
                        vaoChanged = true;
                    }

                    if (vaoChanged || lastBaseInstance != cmd.Command.BaseInstance) {
                        Gl.BindBuffer(GLEnum.ArrayBuffer, _worldInstanceBuffer);
                        var offset = (byte*)0 + (cmd.Command.BaseInstance * sizeof(InstanceData));

                        for (uint j = 0; j < 4; j++) {
                            var loc = 3 + j;
                            if (vaoChanged) {
                                Gl.EnableVertexAttribArray(loc);
                                Gl.VertexAttribDivisor(loc, 1);
                            }
                            Gl.VertexAttribPointer(loc, 4, GLEnum.Float, false, stride, (void*)(offset + (j * 16)));
                        }
                        if (vaoChanged) {
                            Gl.EnableVertexAttribArray(8);
                            Gl.VertexAttribDivisor(8, 1);
                        }
                        Gl.VertexAttribIPointer(8, 1, GLEnum.UnsignedInt, stride, (void*)(offset + 64));

                        lastBaseInstance = cmd.Command.BaseInstance;
                    }

                    if (lastTextureIndex != cmd.TextureIndex) {
                        Gl.VertexAttrib1(7, (float)cmd.TextureIndex);
                        lastTextureIndex = cmd.TextureIndex;
                    }

                    if (CurrentAtlas != (uint)cmd.Atlas.NativePtr) {
                        cmd.Atlas.Bind(0);
                        shader.SetUniform("uTextureArray", 0);
                        CurrentAtlas = (uint)cmd.Atlas.NativePtr;
                    }

                    if (CurrentIBO != cmd.IBO) {
                        Gl.BindBuffer(GLEnum.ElementArrayBuffer, cmd.IBO);
                        CurrentIBO = cmd.IBO;
                    }

                    Gl.DrawElementsInstancedBaseVertex(PrimitiveType.Triangles, cmd.Command.Count,
                        DrawElementsType.UnsignedShort, (void*)(cmd.Command.FirstIndex * sizeof(ushort)), cmd.Command.InstanceCount, (int)cmd.Command.BaseVertex);
                }
            }
        }

        public unsafe void RenderConsolidatedMDI(IShader shader, List<ObjectLandblock> landblocks, int renderPass) {
            if (landblocks.Count == 0) return;

            shader.SetUniform("uFilterByCell", 0);

            for (int i = 0; i < 4; i++) _cullGroups[i].Clear();
            int totalDraws = 0;
            foreach (var lb in landblocks) {
                foreach (var kvp in lb.MdiCommands) {
                    var idx = (int)kvp.Key;
                    if (idx < 0 || idx >= 4) continue;

                    foreach (var cmd in kvp.Value) {
                        if (renderPass == 1 && !cmd.IsTransparent) continue;
                        _cullGroups[idx].Add(cmd);
                        totalDraws++;
                    }
                }
            }

            if (totalDraws == 0) return;

            if (totalDraws > _mdiCommandCapacity) {
                _mdiCommandCapacity = Math.Max(_mdiCommandCapacity * 2, totalDraws);
                Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _mdiCommandBuffer);
                Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(_mdiCommandCapacity * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
                Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernBatchBuffer);
                Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_mdiCommandCapacity * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
            }

            if (_commands.Length < totalDraws) Array.Resize(ref _commands, Math.Max(_commands.Length * 2, totalDraws));
            if (_modernBatches.Length < totalDraws) Array.Resize(ref _modernBatches, Math.Max(_modernBatches.Length * 2, totalDraws));

            int cmdIndex = 0;
            for (int i = 0; i < 4; i++) {
                foreach (var cmd in _cullGroups[i]) {
                    _commands[cmdIndex] = cmd.Command;
                    _modernBatches[cmdIndex] = cmd.BatchData;
                    cmdIndex++;
                }
            }

            // Upload commands and batch data (with orphaning)
            Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _mdiCommandBuffer);
            // Orphan only what we need to avoid excess memory allocation/driver overhead
            Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(totalDraws * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
            fixed (DrawElementsIndirectCommand* ptr = _commands) {
                Gl.BufferSubData(GLEnum.DrawIndirectBuffer, 0, (nuint)(totalDraws * sizeof(DrawElementsIndirectCommand)), ptr);
            }

            Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernBatchBuffer);
            // Orphan only what we need to avoid excess memory allocation/driver overhead
            Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(totalDraws * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
            fixed (ModernBatchData* ptr = _modernBatches) {
                Gl.BufferSubData(GLEnum.ShaderStorageBuffer, 0, (nuint)(totalDraws * sizeof(ModernBatchData)), ptr);
            }

            var globalVao = MeshManager.GlobalBuffer!.VAO;
            if (CurrentVAO != globalVao) {
                Gl.BindVertexArray(globalVao);
                CurrentVAO = globalVao;
            }

            Gl.BindBufferBase(GLEnum.ShaderStorageBuffer, 0, _worldInstanceSSBO);
            Gl.BindBufferBase(GLEnum.ShaderStorageBuffer, 1, _modernBatchBuffer);

            int currentDrawOffset = 0;
            for (int i = 0; i < 4; i++) {
                var group = _cullGroups[i];
                if (group.Count == 0) continue;

                var cullMode = (CullMode)i;
                if (CurrentCullMode != cullMode) {
                    SetCullMode(cullMode);
                    CurrentCullMode = cullMode;
                }

                shader.SetUniform("uDrawIDOffset", currentDrawOffset);
                int numDraws = group.Count;
                Gl.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort,
                    (void*)(currentDrawOffset * sizeof(DrawElementsIndirectCommand)), (uint)numDraws, (uint)sizeof(DrawElementsIndirectCommand));

                currentDrawOffset += numDraws;
            }
            shader.SetUniform("uDrawIDOffset", 0);
        }

        protected unsafe void RenderObjectBatches(IShader shader, ObjectRenderData renderData,
            int instanceCount, int instanceOffset, uint instanceVbo, int renderPass, bool showCulling = true) {
            if (renderData.Batches.Count == 0 || instanceCount == 0) return;

            shader.Bind();
            shader.SetUniform("uFilterByCell", 0);

            bool vaoChanged = false;
            if (CurrentVAO != renderData.VAO) {
                Gl.BindVertexArray(renderData.VAO);
                CurrentVAO = renderData.VAO;
                vaoChanged = true;
            }

            Gl.BindBuffer(GLEnum.ArrayBuffer, instanceVbo);
            var stride = (uint)sizeof(InstanceData);
            var offset = (byte*)0 + (instanceOffset * sizeof(InstanceData));

            for (uint i = 0; i < 4; i++) {
                var loc = 3 + i;
                if (vaoChanged) {
                    Gl.EnableVertexAttribArray(loc);
                    Gl.VertexAttribDivisor(loc, 1);
                }
                Gl.VertexAttribPointer(loc, 4, GLEnum.Float, false, stride, (void*)(offset + (i * 16)));
            }
            if (vaoChanged) {
                Gl.EnableVertexAttribArray(8);
                Gl.VertexAttribDivisor(8, 1);
            }
            Gl.VertexAttribIPointer(8, 1, GLEnum.UnsignedInt, stride, (void*)(offset + 64));

            foreach (var batch in renderData.Batches) {
                if (batch.IsTransparent && renderPass == 0) {
                    // Transparent batches should be rendered in both passes
                }
                else if (batch.IsTransparent != (renderPass == 1)) continue;

                var cullMode = showCulling ? batch.CullMode : CullMode.None;
                if (CurrentCullMode != cullMode) {
                    SetCullMode(cullMode);
                    CurrentCullMode = cullMode;
                }

                Gl.DisableVertexAttribArray(7);
                Gl.VertexAttrib1(7, (float)batch.TextureIndex);

                if (CurrentAtlas != (uint)batch.Atlas.TextureArray.NativePtr) {
                    batch.Atlas.TextureArray.Bind(0);
                    shader.SetUniform("uTextureArray", 0);
                    CurrentAtlas = (uint)batch.Atlas.TextureArray.NativePtr;
                }

                if (CurrentIBO != batch.IBO) {
                    Gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);
                    CurrentIBO = batch.IBO;
                }

                Gl.DrawElementsInstancedBaseVertex(PrimitiveType.Triangles, (uint)batch.IndexCount,
                    DrawElementsType.UnsignedShort, (void*)(batch.FirstIndex * sizeof(ushort)), (uint)instanceCount, (int)batch.BaseVertex);
            }
        }

        protected unsafe void RenderObjectBatches(IShader shader, ObjectRenderData renderData,
            List<InstanceData> instanceTransforms, int renderPass, bool showCulling = true) {
            if (renderData.Batches.Count == 0 || instanceTransforms.Count == 0) return;

            GraphicsDevice.EnsureInstanceBufferCapacity(instanceTransforms.Count, sizeof(InstanceData));
            Gl.BindBuffer(GLEnum.ArrayBuffer, GraphicsDevice.InstanceVBO);

            var transformsSpan = CollectionsMarshal.AsSpan(instanceTransforms);
            fixed (InstanceData* ptr = transformsSpan) {
                Gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(instanceTransforms.Count * sizeof(InstanceData)), ptr);
            }

            RenderObjectBatches(shader, renderData, instanceTransforms.Count, 0, GraphicsDevice.InstanceVBO, renderPass, showCulling);
        }

        protected unsafe void RenderObjectBatches(IShader shader, ObjectRenderData renderData,
            int instanceCount, int instanceOffset, int renderPass, bool showCulling = true) {
            RenderObjectBatches(shader, renderData, instanceCount, instanceOffset, GraphicsDevice.InstanceVBO, renderPass, showCulling);
        }

        protected unsafe void RenderModernMDI(IShader shader, List<(ObjectRenderData renderData, int count, int offset)> drawCalls, List<InstanceData> allInstances, int renderPass, bool showCulling = true) {
            if (drawCalls.Count == 0 || allInstances.Count == 0) return;

            shader.Bind();
            shader.SetUniform("uFilterByCell", 0);

            var batchesByCullMode = new Dictionary<CullMode, List<(ObjectRenderBatch batch, int instanceCount, int instanceOffset)>>();
            int totalDraws = 0;

            foreach (var call in drawCalls) {
                foreach (var batch in call.renderData.Batches) {
                    if (batch.IsTransparent && renderPass == 0) {
                        // Transparent batches should be rendered in both passes
                    }
                    else if (batch.IsTransparent != (renderPass == 1)) continue;

                    var cullMode = showCulling ? batch.CullMode : CullMode.None;
                    if (!batchesByCullMode.TryGetValue(cullMode, out var list)) {
                        list = new();
                        batchesByCullMode[cullMode] = list;
                    }
                    list.Add((batch, call.count, call.offset));
                    totalDraws++;
                }
            }

            if (totalDraws == 0) return;

            // Resize buffers if needed
            if (totalDraws > _mdiCommandCapacity) {
                _mdiCommandCapacity = Math.Max(_mdiCommandCapacity * 2, totalDraws);
                Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _mdiCommandBuffer);
                Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(_mdiCommandCapacity * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
                Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernBatchBuffer);
                Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_mdiCommandCapacity * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
            }

            int uniqueInstanceCount = allInstances.Count;
            if (uniqueInstanceCount > _modernInstanceCapacity) {
                _modernInstanceCapacity = Math.Max(_modernInstanceCapacity * 2, uniqueInstanceCount);
                Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernInstanceBuffer);
                Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_modernInstanceCapacity * sizeof(InstanceData)), null, GLEnum.DynamicDraw);
            }

            if (_commands.Length < totalDraws) Array.Resize(ref _commands, Math.Max(_commands.Length * 2, totalDraws));
            if (_modernBatches.Length < totalDraws) Array.Resize(ref _modernBatches, Math.Max(_modernBatches.Length * 2, totalDraws));

            // 2. Build commands
            int cmdIndex = 0;
            foreach (var group in batchesByCullMode) {
                foreach (var item in group.Value) {
                    _modernBatches[cmdIndex] = new ModernBatchData {
                        TextureHandle = item.batch.BindlessTextureHandle,
                        TextureIndex = (uint)item.batch.TextureIndex
                    };

                    _commands[cmdIndex] = new DrawElementsIndirectCommand {
                        Count = (uint)item.batch.IndexCount,
                        InstanceCount = (uint)item.instanceCount,
                        FirstIndex = item.batch.FirstIndex,
                        BaseVertex = (int)item.batch.BaseVertex,
                        BaseInstance = (uint)item.instanceOffset
                    };
                    cmdIndex++;
                }
            }

            // 3. Upload (with orphaning)
            Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _mdiCommandBuffer);
            // Orphan only what we need to avoid excess memory allocation/driver overhead
            Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(totalDraws * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
            fixed (DrawElementsIndirectCommand* ptr = _commands) {
                Gl.BufferSubData(GLEnum.DrawIndirectBuffer, 0, (nuint)(totalDraws * sizeof(DrawElementsIndirectCommand)), ptr);
            }

            Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernInstanceBuffer);
            // Orphan only what we need to avoid excess memory allocation/driver overhead
            Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(uniqueInstanceCount * sizeof(InstanceData)), null, GLEnum.DynamicDraw);
            var instancesSpan = CollectionsMarshal.AsSpan(allInstances);
            fixed (InstanceData* ptr = instancesSpan) {
                Gl.BufferSubData(GLEnum.ShaderStorageBuffer, 0, (nuint)(uniqueInstanceCount * sizeof(InstanceData)), ptr);
            }

            Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernBatchBuffer);
            // Orphan only what we need to avoid excess memory allocation/driver overhead
            Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(totalDraws * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
            fixed (ModernBatchData* ptr = _modernBatches) {
                Gl.BufferSubData(GLEnum.ShaderStorageBuffer, 0, (nuint)(totalDraws * sizeof(ModernBatchData)), ptr);
            }

            // 4. Draw
            var globalVao = MeshManager.GlobalBuffer!.VAO;
            if (CurrentVAO != globalVao) {
                Gl.BindVertexArray(globalVao);
                CurrentVAO = globalVao;
            }

            Gl.BindBufferBase(GLEnum.ShaderStorageBuffer, 0, _modernInstanceBuffer);
            Gl.BindBufferBase(GLEnum.ShaderStorageBuffer, 1, _modernBatchBuffer);

            int currentDrawOffset = 0;
            foreach (var group in batchesByCullMode) {
                if (CurrentCullMode != group.Key) {
                    SetCullMode(group.Key);
                    CurrentCullMode = group.Key;
                }

                shader.SetUniform("uDrawIDOffset", currentDrawOffset);
                int numDraws = group.Value.Count;
                Gl.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort,
                    (void*)(currentDrawOffset * sizeof(DrawElementsIndirectCommand)), (uint)numDraws, (uint)sizeof(DrawElementsIndirectCommand));

                currentDrawOffset += numDraws;
            }
            shader.SetUniform("uDrawIDOffset", 0);
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

        public virtual unsafe void Dispose() {
            var name = GetType().Name;
            GpuMemoryTracker.UntrackNamedBuffer($"{name} World Instance VBO");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} World Instance SSBO");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} MDI Command Buffer");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} Modern Batch Buffer");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} Modern Instance Buffer");

            if (_mdiCommandBuffer != 0) {
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                GpuMemoryTracker.TrackDeallocation(_mdiCommandCapacity * sizeof(DrawElementsIndirectCommand), GpuResourceType.Buffer);
                Gl.DeleteBuffer(_mdiCommandBuffer);
            }
            if (_modernBatchBuffer != 0) {
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                GpuMemoryTracker.TrackDeallocation(_mdiCommandCapacity * sizeof(ModernBatchData), GpuResourceType.Buffer);
                Gl.DeleteBuffer(_modernBatchBuffer);
            }
            if (_modernInstanceBuffer != 0) {
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                GpuMemoryTracker.TrackDeallocation(_modernInstanceCapacity * sizeof(InstanceData), GpuResourceType.Buffer);
                Gl.DeleteBuffer(_modernInstanceBuffer);
            }
            if (_worldInstanceBuffer != 0) {
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                GpuMemoryTracker.TrackDeallocation(_worldInstanceCapacity * sizeof(InstanceData), GpuResourceType.Buffer);
                Gl.DeleteBuffer(_worldInstanceBuffer);
            }
            if (_worldInstanceSSBO != 0) {
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                GpuMemoryTracker.TrackDeallocation(_worldInstanceCapacity * sizeof(InstanceData), GpuResourceType.Buffer);
                Gl.DeleteBuffer(_worldInstanceSSBO);
            }
        }
    }
}
