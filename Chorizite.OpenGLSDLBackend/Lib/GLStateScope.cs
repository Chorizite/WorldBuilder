using Silk.NET.OpenGL;
using System;

namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// A RAII scope for saving and restoring OpenGL state.
    /// </summary>
    public unsafe struct GLStateScope : IDisposable {
        private readonly GL _gl;
        private fixed int _viewport[4];
        private bool _scissorTest;
        private fixed int _scissorBox[4];
        private bool _depthTest;
        private int _depthFunc;
        private bool _depthMask;
        private bool _cullFace;
        private int _cullFaceMode;
        private int _frontFace;
        private bool _blend;
        private int _blendSrc;
        private int _blendDst;
        private int _blendEquation;
        
        // Extended state
        private int _blendSrcAlpha;
        private int _blendDstAlpha;
        private int _blendEquationAlpha;
        private fixed byte _colorMask[4];
        private fixed float _clearColor[4];
        private float _clearDepth;
        private int _currentProgram;
        private int _vertexArrayBinding;
        private int _arrayBufferBinding;
        private int _elementArrayBufferBinding;
        private int _activeTexture;
        private int _textureBinding2D;
        private bool _stencilTest;
        private int _stencilFunc;
        private int _stencilRef;
        private int _stencilValueMask;
        private int _stencilFail;
        private int _stencilPassDepthFail;
        private int _stencilPassDepthPass;
        private int _stencilWritemask;
        private int _unpackAlignment;
        private int _packAlignment;
        
        private int _drawFramebufferBinding;
        
        // Skia / Avalonia extra state protections
        private fixed float _blendColor[4];
        private int _polygonMode;
        private bool _sampleAlphaToCoverage;
        private bool _multisample;
        private bool _primitiveRestart;
        private int _readFramebufferBinding;
        private int _uniformBufferBinding0;
        private float _lineWidth;
        private bool _programPointSize;
        private int _samplerBinding0;
        private int _samplerBinding1;
        private int _samplerBinding2;
        private int _unpackRowLength;
        private int _unpackSkipRows;
        private int _unpackSkipPixels;
        private bool _sampleAlphaToOne;
        
        private bool _isDisposed;

        /// <summary>
        /// Captures the current OpenGL state.
        /// </summary>
        /// <param name="gl"></param>
        public GLStateScope(GL gl) {
            _gl = gl;
            _isDisposed = false;

            fixed (int* v = _viewport) _gl.GetInteger(GetPName.Viewport, v);
            _scissorTest = _gl.IsEnabled(EnableCap.ScissorTest);
            fixed (int* s = _scissorBox) _gl.GetInteger(GetPName.ScissorBox, s);
            
            _depthTest = _gl.IsEnabled(EnableCap.DepthTest);
            _gl.GetInteger(GetPName.DepthFunc, out _depthFunc);
            byte depthMask = 0;
            _gl.GetBoolean((GetPName)GLEnum.DepthWritemask, (bool*)&depthMask);
            _depthMask = depthMask != 0;

            _cullFace = _gl.IsEnabled(EnableCap.CullFace);
            _gl.GetInteger(GetPName.CullFaceMode, out _cullFaceMode);
            _gl.GetInteger(GetPName.FrontFace, out _frontFace);
            
            _blend = _gl.IsEnabled(EnableCap.Blend);
            _gl.GetInteger(GetPName.BlendSrcRgb, out _blendSrc);
            _gl.GetInteger(GetPName.BlendDstRgb, out _blendDst);
            _gl.GetInteger(GetPName.BlendSrcAlpha, out _blendSrcAlpha);
            _gl.GetInteger(GetPName.BlendDstAlpha, out _blendDstAlpha);
            _gl.GetInteger(GetPName.BlendEquationRgb, out _blendEquation);
            _gl.GetInteger(GetPName.BlendEquationAlpha, out _blendEquationAlpha);
            
            fixed (byte* c = _colorMask) _gl.GetBoolean((GetPName)GLEnum.ColorWritemask, (bool*)c);
            fixed (float* cc = _clearColor) _gl.GetFloat(GetPName.ColorClearValue, cc);
            _gl.GetFloat(GetPName.DepthClearValue, out _clearDepth);

            _gl.GetInteger(GetPName.CurrentProgram, out _currentProgram);
            _gl.GetInteger(GetPName.VertexArrayBinding, out _vertexArrayBinding);
            _gl.GetInteger(GetPName.ArrayBufferBinding, out _arrayBufferBinding);
            _gl.GetInteger(GetPName.ElementArrayBufferBinding, out _elementArrayBufferBinding);

            _gl.GetInteger(GetPName.ActiveTexture, out _activeTexture);
            _gl.GetInteger(GetPName.TextureBinding2D, out _textureBinding2D);

            _stencilTest = _gl.IsEnabled(EnableCap.StencilTest);
            _gl.GetInteger(GetPName.StencilFunc, out _stencilFunc);
            _gl.GetInteger(GetPName.StencilRef, out _stencilRef);
            _gl.GetInteger(GetPName.StencilValueMask, out _stencilValueMask);
            _gl.GetInteger(GetPName.StencilFail, out _stencilFail);
            _gl.GetInteger(GetPName.StencilPassDepthFail, out _stencilPassDepthFail);
            _gl.GetInteger(GetPName.StencilPassDepthPass, out _stencilPassDepthPass);
            _gl.GetInteger(GetPName.StencilWritemask, out _stencilWritemask);

            _gl.GetInteger(GetPName.UnpackAlignment, out _unpackAlignment);
            _gl.GetInteger(GetPName.PackAlignment, out _packAlignment);

            _gl.GetInteger(GetPName.DrawFramebufferBinding, out _drawFramebufferBinding);
            
            fixed (float* bc = _blendColor) _gl.GetFloat(GetPName.BlendColor, bc);
            _gl.GetInteger(GetPName.PolygonMode, out _polygonMode);
            _sampleAlphaToCoverage = _gl.IsEnabled(EnableCap.SampleAlphaToCoverage);
            _multisample = _gl.IsEnabled(EnableCap.Multisample);
            _primitiveRestart = _gl.IsEnabled((EnableCap)GLEnum.PrimitiveRestart);
            _gl.GetInteger(GetPName.ReadFramebufferBinding, out _readFramebufferBinding);
            
            _gl.GetInteger(GetPName.UniformBufferBinding, out _uniformBufferBinding0);
            
            _gl.GetFloat(GetPName.LineWidth, out _lineWidth);
            _programPointSize = _gl.IsEnabled((EnableCap)GLEnum.ProgramPointSize);

            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.GetInteger((GetPName)GLEnum.SamplerBinding, out _samplerBinding0);
            _gl.ActiveTexture(TextureUnit.Texture1);
            _gl.GetInteger((GetPName)GLEnum.SamplerBinding, out _samplerBinding1);
            _gl.ActiveTexture(TextureUnit.Texture2);
            _gl.GetInteger((GetPName)GLEnum.SamplerBinding, out _samplerBinding2);
            _gl.ActiveTexture((TextureUnit)_activeTexture);

            _gl.GetInteger((GetPName)GLEnum.UnpackRowLength, out _unpackRowLength);
            _gl.GetInteger((GetPName)GLEnum.UnpackSkipRows, out _unpackSkipRows);
            _gl.GetInteger((GetPName)GLEnum.UnpackSkipPixels, out _unpackSkipPixels);
            _sampleAlphaToOne = _gl.IsEnabled(EnableCap.SampleAlphaToOne);
        }

        /// <summary>
        /// Restores only the scissor state from the scope.
        /// </summary>
        public void RestoreScissor() {
            if (_scissorTest) _gl.Enable(EnableCap.ScissorTest); 
            else _gl.Disable(EnableCap.ScissorTest);
            _gl.Scissor(_scissorBox[0], _scissorBox[1], (uint)_scissorBox[2], (uint)_scissorBox[3]);
        }

        /// <summary>
        /// Restores the captured OpenGL state.
        /// </summary>
        public void Dispose() {
            if (_isDisposed) return;

            // Restoring state
            if (_currentProgram != 0) _gl.UseProgram((uint)_currentProgram); else _gl.UseProgram(0);

            _gl.BindVertexArray((uint)_vertexArrayBinding);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)_arrayBufferBinding);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, (uint)_elementArrayBufferBinding);
            _gl.BindBuffer(GLEnum.UniformBuffer, (uint)_uniformBufferBinding0);

            _gl.ActiveTexture((TextureUnit)_activeTexture);
            _gl.BindTexture(TextureTarget.Texture2D, (uint)_textureBinding2D);

            if (_stencilTest) _gl.Enable(EnableCap.StencilTest); else _gl.Disable(EnableCap.StencilTest);
            _gl.StencilFunc((StencilFunction)_stencilFunc, _stencilRef, (uint)_stencilValueMask);
            _gl.StencilOp((StencilOp)_stencilFail, (StencilOp)_stencilPassDepthFail, (StencilOp)_stencilPassDepthPass);
            _gl.StencilMask((uint)_stencilWritemask);

            _gl.PixelStore(PixelStoreParameter.UnpackAlignment, _unpackAlignment);
            _gl.PixelStore(PixelStoreParameter.PackAlignment, _packAlignment);
            _gl.PixelStore(PixelStoreParameter.UnpackRowLength, _unpackRowLength);
            _gl.PixelStore(PixelStoreParameter.UnpackSkipRows, _unpackSkipRows);
            _gl.PixelStore(PixelStoreParameter.UnpackSkipPixels, _unpackSkipPixels);

            _gl.ClearColor(_clearColor[0], _clearColor[1], _clearColor[2], _clearColor[3]);
            _gl.ClearDepth(_clearDepth);

            _gl.Viewport(_viewport[0], _viewport[1], (uint)_viewport[2], (uint)_viewport[3]);
            RestoreScissor();
            
            if (_depthTest) _gl.Enable(EnableCap.DepthTest); else _gl.Disable(EnableCap.DepthTest);
            _gl.DepthFunc((DepthFunction)_depthFunc);
            _gl.DepthMask(_depthMask);
            
            if (_cullFace) _gl.Enable(EnableCap.CullFace); else _gl.Disable(EnableCap.CullFace);
            _gl.CullFace((TriangleFace)_cullFaceMode);
            _gl.FrontFace((FrontFaceDirection)_frontFace);
            
            if (_blend) _gl.Enable(EnableCap.Blend); else _gl.Disable(EnableCap.Blend);
            _gl.BlendFuncSeparate((BlendingFactor)_blendSrc, (BlendingFactor)_blendDst, (BlendingFactor)_blendSrcAlpha, (BlendingFactor)_blendDstAlpha);
            _gl.BlendEquationSeparate((BlendEquationModeEXT)_blendEquation, (BlendEquationModeEXT)_blendEquationAlpha);
            _gl.BlendColor(_blendColor[0], _blendColor[1], _blendColor[2], _blendColor[3]);
            
            _gl.ColorMask(_colorMask[0] != 0, _colorMask[1] != 0, _colorMask[2] != 0, _colorMask[3] != 0);
            
            _gl.PolygonMode(GLEnum.FrontAndBack, (PolygonMode)_polygonMode);
            
            if (_sampleAlphaToCoverage) _gl.Enable(EnableCap.SampleAlphaToCoverage); else _gl.Disable(EnableCap.SampleAlphaToCoverage);
            if (_sampleAlphaToOne) _gl.Enable(EnableCap.SampleAlphaToOne); else _gl.Disable(EnableCap.SampleAlphaToOne);
            if (_multisample) _gl.Enable(EnableCap.Multisample); else _gl.Disable(EnableCap.Multisample);
            if (_primitiveRestart) _gl.Enable((EnableCap)GLEnum.PrimitiveRestart); else _gl.Disable((EnableCap)GLEnum.PrimitiveRestart);
            if (_programPointSize) _gl.Enable((EnableCap)GLEnum.ProgramPointSize); else _gl.Disable((EnableCap)GLEnum.ProgramPointSize);
            
            _gl.LineWidth(_lineWidth);

            _gl.BindSampler(0, (uint)_samplerBinding0);
            _gl.BindSampler(1, (uint)_samplerBinding1);
            _gl.BindSampler(2, (uint)_samplerBinding2);

            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)_drawFramebufferBinding);
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)_readFramebufferBinding);

            _isDisposed = true;
        }
    }
}
