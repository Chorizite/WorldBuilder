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
        public bool IsDisposed { get; private set; }
        protected readonly GL Gl;
        protected readonly OpenGLGraphicsDevice GraphicsDevice;
        protected readonly ObjectMeshManager MeshManager;
        protected readonly bool _useModernRendering;

        // Render state tracking (Static so all managers sharing a context see the same state)
        public static uint CurrentVAO;
        public static uint CurrentIBO;
        public static uint CurrentAtlas;
        public static uint CurrentInstanceBuffer;
        public static CullMode? CurrentCullMode;

        // Global instance buffers for all landblocks managed by this manager
        private uint _worldInstanceBuffer;
        private uint _worldInstanceSSBO;
        private int _worldInstanceCapacity = 4096;
        private readonly List<(int Offset, int Size)> _freeSlices = new();
        private readonly object _allocationLock = new();

        // Modern rendering MDI buffers
        private readonly uint[] _mdiCommandBuffers = new uint[3];
        private readonly int[] _mdiCommandCapacities = [1024, 1024, 1024];
        private readonly uint[] _modernBatchBuffers = new uint[3];

        // Scratch buffers for dynamic updates (highlights, selections, etc)
        private readonly uint[] _scratchMdiCommandBuffers = new uint[3];
        private readonly int[] _scratchMdiCommandCapacities = [256, 256, 256];
        private readonly uint[] _scratchModernBatchBuffers = new uint[3];

        private readonly uint[] _modernInstanceBuffers = new uint[3]; // Scratch buffer for non-consolidated modern draws
        private readonly int[] _modernInstanceCapacities = [1024, 1024, 1024];

        // MDI dirty tracking — when false, RenderConsolidatedMDI skips buffer orphaning
        // and reuses the previously uploaded GPU buffers. This avoids driver-specific issues
        // with constant BufferData(null) orphaning when the app is idle/backgrounded.
        protected bool _mdiDirty = true;
        private int _lastOpaqueDrawCount;
        private int _lastTransparentDrawCount;

        // Reusable arrays to avoid allocations per frame
        private DrawElementsIndirectCommand[] _commands = Array.Empty<DrawElementsIndirectCommand>();
        private ModernBatchData[] _modernBatches = Array.Empty<ModernBatchData>();
        private readonly List<LandblockMdiCommand>[] _cullGroups = [new(), new(), new(), new()];

        protected unsafe BaseObjectRenderManager(GL gl, OpenGLGraphicsDevice graphicsDevice, ObjectMeshManager meshManager, bool createWorldBuffer = true, int initialCapacity = 4096) {
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

                for (int i = 0; i < 3; i++) {
                    Gl.GenBuffers(1, out _mdiCommandBuffers[i]);
                    Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _mdiCommandBuffers[i]);
                    Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(_mdiCommandCapacities[i] * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
                    GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                    GpuMemoryTracker.TrackAllocation(_mdiCommandCapacities[i] * sizeof(DrawElementsIndirectCommand), GpuResourceType.Buffer);

                    Gl.GenBuffers(1, out _modernBatchBuffers[i]);
                    Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernBatchBuffers[i]);
                    Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_mdiCommandCapacities[i] * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
                    GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                    GpuMemoryTracker.TrackAllocation(_mdiCommandCapacities[i] * sizeof(ModernBatchData), GpuResourceType.Buffer);
                }

                for (int i = 0; i < 3; i++) {
                    Gl.GenBuffers(1, out _scratchMdiCommandBuffers[i]);
                    Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _scratchMdiCommandBuffers[i]);
                    Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(_scratchMdiCommandCapacities[i] * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
                    GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                    GpuMemoryTracker.TrackAllocation(_scratchMdiCommandCapacities[i] * sizeof(DrawElementsIndirectCommand), GpuResourceType.Buffer);

                    Gl.GenBuffers(1, out _scratchModernBatchBuffers[i]);
                    Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _scratchModernBatchBuffers[i]);
                    Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_scratchMdiCommandCapacities[i] * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
                    GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                    GpuMemoryTracker.TrackAllocation(_scratchMdiCommandCapacities[i] * sizeof(ModernBatchData), GpuResourceType.Buffer);
                }

                for (int i = 0; i < 3; i++) {
                    Gl.GenBuffers(1, out _modernInstanceBuffers[i]);
                    Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernInstanceBuffers[i]);
                    Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_modernInstanceCapacities[i] * sizeof(InstanceData)), null, GLEnum.DynamicDraw);
                    GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                    GpuMemoryTracker.TrackAllocation(_modernInstanceCapacities[i] * sizeof(InstanceData), GpuResourceType.Buffer);
                }
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

                long totalMdiCommandCapacity = _mdiCommandCapacities[0] + _mdiCommandCapacities[1] + _mdiCommandCapacities[2];
                long totalScratchMdiCommandCapacity = _scratchMdiCommandCapacities[0] + _scratchMdiCommandCapacities[1] + _scratchMdiCommandCapacities[2];
                long totalModernInstanceCapacity = _modernInstanceCapacities[0] + _modernInstanceCapacities[1] + _modernInstanceCapacities[2];

                GpuMemoryTracker.TrackNamedBuffer($"{name} MDI Command Buffer",
                    totalMdiCommandCapacity * sizeof(DrawElementsIndirectCommand),
                    0);

                GpuMemoryTracker.TrackNamedBuffer($"{name} Modern Batch Buffer",
                    totalMdiCommandCapacity * sizeof(ModernBatchData),
                    0);

                GpuMemoryTracker.TrackNamedBuffer($"{name} Scratch MDI Command Buffer",
                    totalScratchMdiCommandCapacity * sizeof(DrawElementsIndirectCommand),
                    0);

                GpuMemoryTracker.TrackNamedBuffer($"{name} Scratch Modern Batch Buffer",
                    totalScratchMdiCommandCapacity * sizeof(ModernBatchData),
                    0);

                GpuMemoryTracker.TrackNamedBuffer($"{name} Modern Instance Buffer",
                    totalModernInstanceCapacity * sizeof(InstanceData),
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

            // No free slice found — grow the buffer and retry
            int neededCapacity = _worldInstanceCapacity + count;
            int newCapacity = _worldInstanceCapacity;
            while (newCapacity < neededCapacity) {
                newCapacity = Math.Max(newCapacity * 2, neededCapacity);
            }
            GrowInstanceBuffer(newCapacity);

            // Retry allocation after growing
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
            return -1; // Should not happen after grow
        }

        /// <summary>
        /// Grows the world instance VBO (and SSBO if modern rendering) to the new capacity.
        /// Existing data is preserved via glCopyBufferSubData.
        /// </summary>
        private unsafe void GrowInstanceBuffer(int newCapacity) {
            if (newCapacity <= _worldInstanceCapacity) return;

            int oldCapacity = _worldInstanceCapacity;
            int oldDataSize = oldCapacity * sizeof(InstanceData);
            int newDataSize = newCapacity * sizeof(InstanceData);

            // --- Grow VBO ---
            if (_worldInstanceBuffer != 0) {
                uint newBuffer;
                Gl.GenBuffers(1, out newBuffer);
                GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                Gl.BindBuffer(GLEnum.CopyWriteBuffer, newBuffer);
                Gl.BufferData(GLEnum.CopyWriteBuffer, (nuint)newDataSize, null, GLEnum.DynamicDraw);
                GpuMemoryTracker.TrackAllocation(newDataSize, GpuResourceType.Buffer);

                // Copy existing data
                Gl.BindBuffer(GLEnum.CopyReadBuffer, _worldInstanceBuffer);
                Gl.CopyBufferSubData(GLEnum.CopyReadBuffer, GLEnum.CopyWriteBuffer, 0, 0, (nuint)oldDataSize);

                // Delete old buffer
                Gl.DeleteBuffer(_worldInstanceBuffer);
                GpuMemoryTracker.TrackDeallocation(oldDataSize, GpuResourceType.Buffer);
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);

                _worldInstanceBuffer = newBuffer;
            }

            // --- Grow SSBO ---
            if (_useModernRendering && _worldInstanceSSBO != 0) {
                uint newSSBO;
                Gl.GenBuffers(1, out newSSBO);
                GpuMemoryTracker.TrackResourceAllocation(GpuResourceType.Buffer);
                Gl.BindBuffer(GLEnum.CopyWriteBuffer, newSSBO);
                Gl.BufferData(GLEnum.CopyWriteBuffer, (nuint)newDataSize, null, GLEnum.DynamicDraw);
                GpuMemoryTracker.TrackAllocation(newDataSize, GpuResourceType.Buffer);

                // Copy existing data
                Gl.BindBuffer(GLEnum.CopyReadBuffer, _worldInstanceSSBO);
                Gl.CopyBufferSubData(GLEnum.CopyReadBuffer, GLEnum.CopyWriteBuffer, 0, 0, (nuint)oldDataSize);

                // Delete old buffer
                Gl.DeleteBuffer(_worldInstanceSSBO);
                GpuMemoryTracker.TrackDeallocation(oldDataSize, GpuResourceType.Buffer);
                GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);

                _worldInstanceSSBO = newSSBO;
            }

            // Add new free region at the end
            lock (_allocationLock) {
                _freeSlices.Add((oldCapacity, newCapacity - oldCapacity));
            }

            _worldInstanceCapacity = newCapacity;
            _mdiDirty = true;
            UpdateGpuStats();
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

        public unsafe void RenderConsolidated(IShader shader, List<ObjectLandblock> landblocks, RenderPass renderPass) {
            if (landblocks.Count == 0) return;

            shader.Bind();
            shader.SetUniform("uFilterByCell", 0);

            for (int i = 0; i < 4; i++) _cullGroups[i].Clear();
            foreach (var lb in landblocks) {
                lock (lb) {
                    foreach (var kvp in lb.MdiCommands) {
                        var idx = (int)kvp.Key;
                        if (idx < 0 || idx >= 4) continue;
                        _cullGroups[idx].AddRange(kvp.Value);
                    }
                }
            }

            var stride = (uint)sizeof(InstanceData);
            for (int i = 0; i < 4; i++) {
                var group = _cullGroups[i];
                if (group.Count == 0) continue;

                // Sort to minimize state changes using pre-calculated sort key
                if (renderPass == RenderPass.Transparent) {
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
                    if (renderPass == RenderPass.Transparent && !cmd.IsTransparent) continue;

                    bool vaoChanged = false;
                    if (CurrentVAO != cmd.VAO) {
                        Gl.BindVertexArray(cmd.VAO);
                        CurrentVAO = cmd.VAO;
                        vaoChanged = true;
                    }

                    if (vaoChanged || lastBaseInstance != cmd.Command.BaseInstance || CurrentInstanceBuffer != _worldInstanceBuffer) {
                        Gl.BindBuffer(GLEnum.ArrayBuffer, _worldInstanceBuffer);
                        var offset = (byte*)0 + (cmd.Command.BaseInstance * sizeof(InstanceData));

                        for (uint j = 0; j < 4; j++) {
                            var loc = 3 + j;
                            if (vaoChanged || CurrentInstanceBuffer != _worldInstanceBuffer) {
                                Gl.EnableVertexAttribArray(loc);
                                Gl.VertexAttribDivisor(loc, 1);
                            }
                            Gl.VertexAttribPointer(loc, 4, GLEnum.Float, false, stride, (void*)(offset + (j * 16)));
                        }
                        if (vaoChanged || CurrentInstanceBuffer != _worldInstanceBuffer) {
                            Gl.EnableVertexAttribArray(8);
                            Gl.VertexAttribDivisor(8, 1);
                        }
                        Gl.VertexAttribIPointer(8, 1, GLEnum.UnsignedInt, stride, (void*)(offset + 64));

                        CurrentInstanceBuffer = _worldInstanceBuffer;
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

                    // Bind the correct sampler for wrap vs. clamp based on mesh UV detection
                    var samplerId = cmd.HasWrappingUVs ? GraphicsDevice.WrapSampler : GraphicsDevice.ClampSampler;
                    Gl.BindSampler(0, samplerId);

                    if (CurrentIBO != cmd.IBO) {
                        Gl.BindBuffer(GLEnum.ElementArrayBuffer, cmd.IBO);
                        CurrentIBO = cmd.IBO;
                    }

                    Gl.DrawElementsInstancedBaseVertex(PrimitiveType.Triangles, cmd.Command.Count,
                        DrawElementsType.UnsignedShort, (void*)(cmd.Command.FirstIndex * sizeof(ushort)), cmd.Command.InstanceCount, (int)cmd.Command.BaseVertex);
                }
            }
        }

        /// <summary>
        /// Marks the MDI buffers as dirty, requiring re-upload on next render.
        /// Call this when visible landblocks or their MDI commands change.
        /// </summary>
        public void MarkMdiDirty() {
            _mdiDirty = true;
        }

        public unsafe void RenderConsolidatedMDI(IShader shader, List<ObjectLandblock> landblocks, RenderPass renderPass) {
            if (landblocks.Count == 0) return;

            int passIdx = (int)renderPass;
            if (passIdx < 0 || passIdx > 2) return;

            shader.Bind();
            shader.SetUniform("uFilterByCell", 0);

            // Check if we can skip the buffer upload entirely (same data as last frame)
            ref int lastDrawCount = ref (renderPass == RenderPass.Transparent ? ref _lastTransparentDrawCount : ref _lastOpaqueDrawCount);
            bool needsUpload = _mdiDirty;

            if (needsUpload) {
                for (int i = 0; i < 4; i++) _cullGroups[i].Clear();
                int totalDraws = 0;
                foreach (var lb in landblocks) {
                    lock (lb) {
                        foreach (var kvp in lb.MdiCommands) {
                            var idx = (int)kvp.Key;
                            if (idx < 0 || idx >= 4) continue;

                            foreach (var cmd in kvp.Value) {
                                if (renderPass == RenderPass.Transparent && !cmd.IsTransparent) continue;
                                _cullGroups[idx].Add(cmd);
                                totalDraws++;
                            }
                        }
                    }
                }

                if (totalDraws == 0) {
                    lastDrawCount = 0;
                    return;
                }

                if (totalDraws > _mdiCommandCapacities[passIdx]) {
                    _mdiCommandCapacities[passIdx] = Math.Max(_mdiCommandCapacities[passIdx] * 2, totalDraws);
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
                Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _mdiCommandBuffers[passIdx]);
                Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(_mdiCommandCapacities[passIdx] * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
                fixed (DrawElementsIndirectCommand* ptr = _commands) {
                    Gl.BufferSubData(GLEnum.DrawIndirectBuffer, 0, (nuint)(totalDraws * sizeof(DrawElementsIndirectCommand)), ptr);
                }

                Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernBatchBuffers[passIdx]);
                Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_mdiCommandCapacities[passIdx] * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
                fixed (ModernBatchData* ptr = _modernBatches) {
                    Gl.BufferSubData(GLEnum.ShaderStorageBuffer, 0, (nuint)(totalDraws * sizeof(ModernBatchData)), ptr);
                }

                lastDrawCount = totalDraws;
            }
            else {
                // Nothing changed — reuse previously uploaded buffers
                if (lastDrawCount == 0) return;

                // Rebuild cull groups just for draw offsets (lightweight, no GPU ops)
                for (int i = 0; i < 4; i++) _cullGroups[i].Clear();
                foreach (var lb in landblocks) {
                    lock (lb) {
                        foreach (var kvp in lb.MdiCommands) {
                            var idx = (int)kvp.Key;
                            if (idx < 0 || idx >= 4) continue;
                            foreach (var cmd in kvp.Value) {
                                if (renderPass == RenderPass.Transparent && !cmd.IsTransparent) continue;
                                _cullGroups[idx].Add(cmd);
                            }
                        }
                    }
                }
            }

            var globalVao = MeshManager.GlobalBuffer!.VAO;
            if (globalVao == 0) return;
            if (CurrentVAO != globalVao) {
                Gl.BindVertexArray(globalVao);
                CurrentVAO = globalVao;
            }

            Gl.BindBufferBase(GLEnum.ShaderStorageBuffer, 0, _worldInstanceSSBO);
            Gl.BindBufferBase(GLEnum.ShaderStorageBuffer, 1, _modernBatchBuffers[passIdx]);
            Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _mdiCommandBuffers[passIdx]);

            Gl.MemoryBarrier(MemoryBarrierMask.ShaderStorageBarrierBit | MemoryBarrierMask.CommandBarrierBit);

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
            Gl.BindBuffer(GLEnum.DrawIndirectBuffer, 0);
        }

        protected unsafe void RenderObjectBatches(IShader shader, ObjectRenderData renderData,
            int instanceCount, int instanceOffset, uint instanceVbo, RenderPass renderPass, bool showCulling = true) {
            if (renderData.Batches.Count == 0 || instanceCount == 0) return;

            shader.Bind();
            bool isOutline = shader.Name == "Outline";

            if (!isOutline) {
                var glslShader = shader as GLSLShader;
                if (glslShader != null && glslShader.HasUniform("uFilterByCell")) {
                    shader.SetUniform("uFilterByCell", 0);
                }
            }

            var targetVao = renderData.VAO;
            if (targetVao == 0 && _useModernRendering) {
                targetVao = MeshManager.GlobalBuffer!.VAO;
            }

            if (targetVao != 0) {
                bool vaoChanged = false;
                if (CurrentVAO != targetVao) {
                    Gl.BindVertexArray(targetVao);
                    CurrentVAO = targetVao;
                    vaoChanged = true;
                }

                if (CurrentInstanceBuffer != instanceVbo) {
                    Gl.BindBuffer(GLEnum.ArrayBuffer, instanceVbo);
                    CurrentInstanceBuffer = instanceVbo;
                }

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
                    if (renderPass != RenderPass.SinglePass) {
                        if (batch.IsTransparent && renderPass == RenderPass.Opaque) {
                            // Transparent batches should be rendered in both passes
                        }
                        else if (batch.IsTransparent != (renderPass == RenderPass.Transparent)) continue;
                    }

                    var cullMode = showCulling ? batch.CullMode : CullMode.None;
                    if (CurrentCullMode != cullMode) {
                        SetCullMode(cullMode);
                    }

                    if (CurrentAtlas != (uint)batch.Atlas.TextureArray.NativePtr) {
                        batch.Atlas.TextureArray.Bind(0);
                        shader.SetUniform("uTextureArray", 0);
                        CurrentAtlas = (uint)batch.Atlas.TextureArray.NativePtr;
                    }

                    // Bind the correct sampler for wrap vs. clamp based on mesh UV detection
                    var batchSamplerId = batch.HasWrappingUVs ? GraphicsDevice.WrapSampler : GraphicsDevice.ClampSampler;
                    Gl.BindSampler(0, batchSamplerId);
                    Gl.VertexAttrib1(7, (float)batch.TextureIndex);

                    if (CurrentIBO != batch.IBO) {
                        Gl.BindBuffer(GLEnum.ElementArrayBuffer, batch.IBO);
                        CurrentIBO = batch.IBO;
                    }

                    Gl.DrawElementsInstancedBaseVertex(PrimitiveType.Triangles, (uint)batch.IndexCount,
                        DrawElementsType.UnsignedShort, (void*)(batch.FirstIndex * sizeof(ushort)), (uint)instanceCount, (int)batch.BaseVertex);
                }
            }
        }

        protected unsafe void RenderObjectBatches(IShader shader, ObjectRenderData renderData,
            List<InstanceData> instanceTransforms, RenderPass renderPass, bool showCulling = true) {
            if (renderData.Batches.Count == 0 || instanceTransforms.Count == 0) return;

            GraphicsDevice.UpdateInstanceBuffer(instanceTransforms);
            CurrentInstanceBuffer = GraphicsDevice.InstanceVBO;

            RenderObjectBatches(shader, renderData, instanceTransforms.Count, 0, GraphicsDevice.InstanceVBO, renderPass, showCulling);
        }

        protected unsafe void RenderObjectBatches(IShader shader, ObjectRenderData renderData,
            int instanceCount, int instanceOffset, RenderPass renderPass, bool showCulling = true) {
            RenderObjectBatches(shader, renderData, instanceCount, instanceOffset, GraphicsDevice.InstanceVBO, renderPass, showCulling);
        }

        protected unsafe void RenderModernMDI(IShader shader, List<(ObjectRenderData renderData, int count, int offset)> drawCalls, List<InstanceData> allInstances, RenderPass renderPass, bool showCulling = true) {
            if (drawCalls.Count == 0 || allInstances.Count == 0) return;

            int passIdx = (int)renderPass;
            if (passIdx < 0 || passIdx > 2) return;

            shader.Bind();
            shader.SetUniform("uFilterByCell", 0);

            var batchesByCullMode = new Dictionary<CullMode, List<(ObjectRenderBatch batch, int instanceCount, int instanceOffset)>>();
            int totalDraws = 0;

            foreach (var call in drawCalls) {
                foreach (var batch in call.renderData.Batches) {
                    if (renderPass != RenderPass.SinglePass) {
                        if (batch.IsTransparent && renderPass == RenderPass.Opaque) {
                            // Transparent batches should be rendered in both passes
                        }
                        else if (batch.IsTransparent != (renderPass == RenderPass.Transparent)) continue;
                    }

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
            if (totalDraws > _scratchMdiCommandCapacities[passIdx]) {
                _scratchMdiCommandCapacities[passIdx] = Math.Max(_scratchMdiCommandCapacities[passIdx] * 2, totalDraws);
                Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _scratchMdiCommandBuffers[passIdx]);
                Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(_scratchMdiCommandCapacities[passIdx] * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
                Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _scratchModernBatchBuffers[passIdx]);
                Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_scratchMdiCommandCapacities[passIdx] * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
            }

            int uniqueInstanceCount = allInstances.Count;
            if (uniqueInstanceCount > _modernInstanceCapacities[passIdx]) {
                _modernInstanceCapacities[passIdx] = Math.Max(_modernInstanceCapacities[passIdx] * 2, uniqueInstanceCount);
                Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernInstanceBuffers[passIdx]);
                Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(_modernInstanceCapacities[passIdx] * sizeof(InstanceData)), null, GLEnum.DynamicDraw);
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
            Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _scratchMdiCommandBuffers[passIdx]);
            // Orphan only what we need to avoid excess memory allocation/driver overhead
            Gl.BufferData(GLEnum.DrawIndirectBuffer, (nuint)(totalDraws * sizeof(DrawElementsIndirectCommand)), null, GLEnum.DynamicDraw);
            fixed (DrawElementsIndirectCommand* ptr = _commands) {
                Gl.BufferSubData(GLEnum.DrawIndirectBuffer, 0, (nuint)(totalDraws * sizeof(DrawElementsIndirectCommand)), ptr);
            }

            Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _modernInstanceBuffers[passIdx]);
            // Orphan only what we need to avoid excess memory allocation/driver overhead
            Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(uniqueInstanceCount * sizeof(InstanceData)), null, GLEnum.DynamicDraw);
            var instancesSpan = CollectionsMarshal.AsSpan(allInstances);
            fixed (InstanceData* ptr = instancesSpan) {
                Gl.BufferSubData(GLEnum.ShaderStorageBuffer, 0, (nuint)(uniqueInstanceCount * sizeof(InstanceData)), ptr);
            }

            Gl.BindBuffer(GLEnum.ShaderStorageBuffer, _scratchModernBatchBuffers[passIdx]);
            // Orphan only what we need to avoid excess memory allocation/driver overhead
            Gl.BufferData(GLEnum.ShaderStorageBuffer, (nuint)(totalDraws * sizeof(ModernBatchData)), null, GLEnum.DynamicDraw);
            fixed (ModernBatchData* ptr = _modernBatches) {
                Gl.BufferSubData(GLEnum.ShaderStorageBuffer, 0, (nuint)(totalDraws * sizeof(ModernBatchData)), ptr);
            }

            // 4. Draw
            var globalVao = MeshManager.GlobalBuffer!.VAO;
            if (globalVao == 0) return;
            if (CurrentVAO != globalVao) {
                Gl.BindVertexArray(globalVao);
                CurrentVAO = globalVao;
            }

            Gl.BindBufferBase(GLEnum.ShaderStorageBuffer, 0, _modernInstanceBuffers[passIdx]);
            Gl.BindBufferBase(GLEnum.ShaderStorageBuffer, 1, _scratchModernBatchBuffers[passIdx]);
            Gl.BindBuffer(GLEnum.DrawIndirectBuffer, _scratchMdiCommandBuffers[passIdx]);

            Gl.MemoryBarrier(MemoryBarrierMask.ShaderStorageBarrierBit | MemoryBarrierMask.CommandBarrierBit);

            int currentDrawOffset = 0;
            foreach (var group in batchesByCullMode) {
                if (CurrentCullMode != group.Key) {
                    SetCullMode(group.Key);
                }

                shader.SetUniform("uDrawIDOffset", currentDrawOffset);
                int numDraws = group.Value.Count;
                Gl.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedShort,
                    (void*)(currentDrawOffset * sizeof(DrawElementsIndirectCommand)), (uint)numDraws, (uint)sizeof(DrawElementsIndirectCommand));

                currentDrawOffset += numDraws;
            }
            shader.SetUniform("uDrawIDOffset", 0);
            Gl.BindBuffer(GLEnum.DrawIndirectBuffer, 0);
            shader.SetUniform("uDrawIDOffset", 0);
        }

        protected void SetCullMode(CullMode mode) {
            CurrentCullMode = mode;
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
            if (IsDisposed) return;
            IsDisposed = true;
            var name = GetType().Name;
            GpuMemoryTracker.UntrackNamedBuffer($"{name} World Instance VBO");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} World Instance SSBO");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} MDI Command Buffer");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} Modern Batch Buffer");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} Scratch MDI Command Buffer");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} Scratch Modern Batch Buffer");
            GpuMemoryTracker.UntrackNamedBuffer($"{name} Modern Instance Buffer");

            // Copy IDs to locals for the lambda
            var mdiCommandBuffers = _mdiCommandBuffers.ToArray();
            var mdiCommandCapacities = _mdiCommandCapacities.ToArray();
            var modernBatchBuffers = _modernBatchBuffers.ToArray();
            var scratchMdiCommandBuffers = _scratchMdiCommandBuffers.ToArray();
            var scratchMdiCommandCapacities = _scratchMdiCommandCapacities.ToArray();
            var scratchModernBatchBuffers = _scratchModernBatchBuffers.ToArray();
            var modernInstanceBuffers = _modernInstanceBuffers.ToArray();
            var modernInstanceCapacities = _modernInstanceCapacities.ToArray();
            var worldInstanceBuffer = _worldInstanceBuffer;
            var worldInstanceCapacity = _worldInstanceCapacity;
            var worldInstanceSSBO = _worldInstanceSSBO;

            GraphicsDevice.QueueGLAction(gl => {
                for (int i = 0; i < 3; i++) {
                    if (mdiCommandBuffers[i] != 0) {
                        GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                        GpuMemoryTracker.TrackDeallocation(mdiCommandCapacities[i] * sizeof(DrawElementsIndirectCommand), GpuResourceType.Buffer);
                        gl.DeleteBuffer(mdiCommandBuffers[i]);
                    }
                    if (modernBatchBuffers[i] != 0) {
                        GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                        GpuMemoryTracker.TrackDeallocation(mdiCommandCapacities[i] * sizeof(ModernBatchData), GpuResourceType.Buffer);
                        gl.DeleteBuffer(modernBatchBuffers[i]);
                    }
                }
                for (int i = 0; i < 3; i++) {
                    if (scratchMdiCommandBuffers[i] != 0) {
                        GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                        GpuMemoryTracker.TrackDeallocation(scratchMdiCommandCapacities[i] * sizeof(DrawElementsIndirectCommand), GpuResourceType.Buffer);
                        gl.DeleteBuffer(scratchMdiCommandBuffers[i]);
                    }
                    if (scratchModernBatchBuffers[i] != 0) {
                        GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                        GpuMemoryTracker.TrackDeallocation(scratchMdiCommandCapacities[i] * sizeof(ModernBatchData), GpuResourceType.Buffer);
                        gl.DeleteBuffer(scratchModernBatchBuffers[i]);
                    }
                    if (modernInstanceBuffers[i] != 0) {
                        GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                        GpuMemoryTracker.TrackDeallocation(modernInstanceCapacities[i] * sizeof(InstanceData), GpuResourceType.Buffer);
                        gl.DeleteBuffer(modernInstanceBuffers[i]);
                    }
                }
                if (worldInstanceBuffer != 0) {
                    GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                    GpuMemoryTracker.TrackDeallocation(worldInstanceCapacity * sizeof(InstanceData), GpuResourceType.Buffer);
                    gl.DeleteBuffer(worldInstanceBuffer);
                }
                if (worldInstanceSSBO != 0) {
                    GpuMemoryTracker.TrackResourceDeallocation(GpuResourceType.Buffer);
                    GpuMemoryTracker.TrackDeallocation(worldInstanceCapacity * sizeof(InstanceData), GpuResourceType.Buffer);
                    gl.DeleteBuffer(worldInstanceSSBO);
                }
            });
        }
    }
}
