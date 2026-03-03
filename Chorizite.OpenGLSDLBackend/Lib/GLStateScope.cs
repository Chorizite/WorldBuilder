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
        private fixed byte _colorMask[4];
        private int _drawFramebufferBinding;
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
            _gl.GetInteger(GetPName.BlendEquationRgb, out _blendEquation);
            
            fixed (byte* c = _colorMask) {
                _gl.GetBoolean((GetPName)GLEnum.ColorWritemask, (bool*)c);
            }

            _gl.GetInteger(GetPName.DrawFramebufferBinding, out _drawFramebufferBinding);
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

            _gl.Viewport(_viewport[0], _viewport[1], (uint)_viewport[2], (uint)_viewport[3]);
            RestoreScissor();
            
            if (_depthTest) _gl.Enable(EnableCap.DepthTest); 
            else _gl.Disable(EnableCap.DepthTest);
            _gl.DepthFunc((DepthFunction)_depthFunc);
            _gl.DepthMask(_depthMask);
            
            if (_cullFace) _gl.Enable(EnableCap.CullFace); 
            else _gl.Disable(EnableCap.CullFace);
            _gl.CullFace((TriangleFace)_cullFaceMode);
            _gl.FrontFace((FrontFaceDirection)_frontFace);
            
            if (_blend) _gl.Enable(EnableCap.Blend); 
            else _gl.Disable(EnableCap.Blend);
            _gl.BlendFunc((BlendingFactor)_blendSrc, (BlendingFactor)_blendDst);
            _gl.BlendEquation((BlendEquationModeEXT)_blendEquation);
            
            _gl.ColorMask(_colorMask[0] != 0, _colorMask[1] != 0, _colorMask[2] != 0, _colorMask[3] != 0);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)_drawFramebufferBinding);

            _isDisposed = true;
        }
    }
}
