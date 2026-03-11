using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.OpenGL;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Chorizite.OpenGLSDLBackend {
    public static class GLHelpers {
        public static OpenGLGraphicsDevice? Device { get; set; }
        public static ILogger? Logger { get; set; }

        public static void Init(OpenGLGraphicsDevice device, ILogger logger) {
            Logger = logger;
            Device = device;
        }

#if DEBUG
        private static bool _loggedVersion = false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckErrors(GL gl, bool logErrors = false, [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) {
            var error = gl.GetError();
            if (error != GLEnum.NoError) {
                if (!_loggedVersion) {
                    _loggedVersion = true;
                    var version = gl.GetStringS(GLEnum.Version);
                    var vendor = gl.GetStringS(GLEnum.Vendor);
                    var renderer = gl.GetStringS(GLEnum.Renderer);
                    Logger?.LogInformation($"GL Version: {version}, Vendor: {vendor}, Renderer: {renderer}");
                }
                string errorDetails = GetErrorDetails(error);
                string location = $"{System.IO.Path.GetFileName(callerFile)}::{callerName}:{callerLine}";

                var program = (uint)gl.GetInteger(GLEnum.CurrentProgram);
                var vao = gl.GetInteger(GLEnum.VertexArrayBinding);
                var activeTex = gl.GetInteger(GLEnum.ActiveTexture);
                var threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

                string extraInfo = "";
                if (program != 0) {
                    bool isProgram = gl.IsProgram(program);
                    gl.GetProgram(program, GLEnum.LinkStatus, out int linkStatus);
                    gl.GetProgram(program, GLEnum.DeleteStatus, out int deleteStatus);
                    gl.GetProgram(program, GLEnum.ValidateStatus, out int validateStatus);
                    extraInfo = $", IsProg: {isProgram}, Link: {linkStatus}, Del: {deleteStatus}, Valid: {validateStatus}";
                }

                string message = $"OpenGL Error: {error} ({errorDetails}) at {location}. Thread: {threadId}, Program: {program}{extraInfo}, VAO: {vao}, ActiveTex: {activeTex}";

                Logger?.LogError(message);
                throw new Exception(message);
            }
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckErrors(GL gl, bool logErrors = false, string callerName = "",
            string callerFile = "", int callerLine = 0) {
        }
#endif

        public static string GetErrorDetails(GLEnum error) {
            return error switch {
                GLEnum.InvalidEnum => "Invalid enum - An unacceptable value is specified for an enumerated argument",
                GLEnum.InvalidValue => "Invalid value - A numeric argument is out of range",
                GLEnum.InvalidOperation =>
                    "Invalid operation - The specified operation is not allowed in the current state",
                GLEnum.StackOverflow => "Stack overflow - An operation would cause an internal stack to overflow",
                GLEnum.StackUnderflow => "Stack underflow - An operation would cause an internal stack to underflow",
                GLEnum.OutOfMemory => "Out of memory - There is not enough memory left to execute the command",
                GLEnum.InvalidFramebufferOperation =>
                    "Invalid framebuffer operation - The framebuffer object is not complete",
                GLEnum.ContextLost => "Context lost - The OpenGL context has been lost due to a graphics card reset",
                _ => "Unknown error"
            };
        }

#if DEBUG
        /// <summary>
        /// Checks for OpenGL errors and provides context-specific information
        /// </summary>
        public static void CheckErrorsWithContext(GL gl, string context, [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) {
            var error = gl.GetError();
            if (error != GLEnum.NoError) {
                string errorDetails = GetErrorDetails(error);
                string location = $"{System.IO.Path.GetFileName(callerFile)}::{callerName}:{callerLine}";
                string message = $"OpenGL Error: {error} ({errorDetails})\nContext: {context}\nLocation: {location}";

                Logger?.LogError(message);
                throw new Exception(message);
            }
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckErrorsWithContext(GL gl, string context, string callerName = "",
            string callerFile = "", int callerLine = 0) {
        }
#endif


        /// <summary>
        /// Gets detailed information about the current texture state for debugging
        /// </summary>
        public static string GetTextureDebugInfo(GL gl, GLEnum target) {
            var info = new System.Text.StringBuilder();
            info.AppendLine($"Texture Debug Info for {target}:");

            try {
                gl.GetTextureLevelParameter((uint)gl.GetInteger(GetPName.TextureBinding2DArray), 0,
                    GetTextureParameter.TextureWidth, out int width);
                gl.GetTextureLevelParameter((uint)gl.GetInteger(GetPName.TextureBinding2DArray), 0,
                    GetTextureParameter.TextureHeight, out int height);
                gl.GetTextureLevelParameter((uint)gl.GetInteger(GetPName.TextureBinding2DArray), 0,
                    GetTextureParameter.TextureDepthExt, out int depth);
                gl.GetTextureLevelParameter((uint)gl.GetInteger(GetPName.TextureBinding2DArray), 0,
                    GetTextureParameter.TextureInternalFormat, out int format);

                info.AppendLine($"  Dimensions: {width}x{height}x{depth}");
                info.AppendLine($"  Internal Format: {(InternalFormat)format}");

                gl.GetTexParameter(target, GetTextureParameter.TextureMinFilter, out int minFilter);
                gl.GetTexParameter(target, GetTextureParameter.TextureMagFilter, out int magFilter);
                info.AppendLine($"  Min Filter: {(TextureMinFilter)minFilter}");
                info.AppendLine($"  Mag Filter: {(TextureMagFilter)magFilter}");

                // Get max mipmap level
                gl.GetTexParameter(target, GetTextureParameter.TextureMaxLevelSgis, out int maxLevel);
                info.AppendLine($"  Max Level: {maxLevel}");

                // Check completeness
                int maxMipLevel = (int)Math.Floor(Math.Log2(Math.Max(width, height)));
                info.AppendLine($"  Calculated Max Mip Level: {maxMipLevel}");
            }
            catch (Exception ex) {
                info.AppendLine($"  Error getting texture info: {ex.Message}");
            }

            return info.ToString();
        }

        /// <summary>
        /// Validates texture completeness for mipmapping
        /// </summary>
        public static bool ValidateTextureMipmapStatus(GL gl, GLEnum target, out string errorMessage) {
            try {
                gl.GetTexLevelParameter(target, 0, GetTextureParameter.TextureWidth, out int width);
                gl.GetTexLevelParameter(target, 0, GetTextureParameter.TextureHeight, out int height);
                gl.GetTexLevelParameter(target, 0, GetTextureParameter.TextureInternalFormat, out int format);

                if (width == 0 || height == 0) {
                    errorMessage = "Texture has zero dimensions";
                    return false;
                }

                // Check if format is valid for mipmap generation
                var internalFormat = (InternalFormat)format;
                if (IsCompressedFormat(internalFormat)) {
                    errorMessage = $"Compressed format {internalFormat} does not support automatic mipmap generation";
                    return false;
                }

                errorMessage = String.Empty;
                return true;
            }
            catch (Exception ex) {
                errorMessage = $"Exception during validation: {ex.Message}";
                return false;
            }
        }

        private static bool IsCompressedFormat(InternalFormat format) {
            return format == InternalFormat.CompressedRgbaS3TCDxt1Ext ||
                   format == InternalFormat.CompressedRgbaS3TCDxt3Ext ||
                   format == InternalFormat.CompressedRgbaS3TCDxt5Ext ||
                   format == InternalFormat.CompressedRgbS3TCDxt1Ext ||
                   format == InternalFormat.CompressedSrgbAlphaS3TCDxt1Ext ||
                   format == InternalFormat.CompressedSrgbAlphaS3TCDxt3Ext ||
                   format == InternalFormat.CompressedSrgbAlphaS3TCDxt5Ext;
        }

        /// <summary>
        /// Logs current OpenGL state for debugging
        /// </summary>
        public static void LogGLState(GL gl, string context = "") {
            var state = new System.Text.StringBuilder();
            state.AppendLine($"=== OpenGL State ({context}) ===");

            try {
                state.AppendLine(
                    $"Active Texture Unit: GL_TEXTURE{gl.GetInteger(GetPName.ActiveTexture) - (int)GLEnum.Texture0}");
                state.AppendLine($"Bound 2D Array Texture: {gl.GetInteger(GetPName.TextureBinding2DArray)}");
                state.AppendLine($"Current Program: {gl.GetInteger(GetPName.CurrentProgram)}");

                gl.GetInteger(GetPName.MaxTextureSize, out int maxTexSize);
                state.AppendLine($"Max Texture Size: {maxTexSize}");

                gl.GetInteger(GetPName.Max3DTextureSize, out int max3DSize);
                state.AppendLine($"Max 3D Texture Size: {max3DSize}");

                gl.GetInteger(GetPName.MaxArrayTextureLayers, out int maxLayers);
                state.AppendLine($"Max Array Texture Layers: {maxLayers}");
            }
            catch (Exception ex) {
                state.AppendLine($"Error getting GL state: {ex.Message}");
            }

            state.AppendLine("======================");
            Logger?.LogInformation(state.ToString());
        }

        /// <summary>
        /// Explicit defaults to prevent Avalonia state leakage into our custom rendering pipeline.
        /// Call this at the start of complex render cycles immediately inside a GLStateScope.
        /// </summary>
        public static void SetupDefaultRenderState(GL gl) {
            gl.BindSampler(0, 0);
            gl.BindSampler(1, 0);
            gl.BindSampler(2, 0);

            gl.ActiveTexture(TextureUnit.Texture1);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture2);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            gl.ActiveTexture(TextureUnit.Texture0); // End on Texture0
            gl.BindTexture(TextureTarget.Texture2D, 0);

            gl.BindVertexArray(0);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
            gl.UseProgram(0);
            
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            gl.PixelStore(PixelStoreParameter.UnpackSkipRows, 0);
            gl.PixelStore(PixelStoreParameter.UnpackSkipPixels, 0);

            gl.Disable(EnableCap.StencilTest);
            gl.BlendColor(0, 0, 0, 0);
            gl.PolygonMode(GLEnum.FrontAndBack, PolygonMode.Fill);

            // Disable Avalonia/Skia specific states
            gl.Disable(EnableCap.SampleAlphaToCoverage);
            gl.Disable(EnableCap.SampleAlphaToOne);
            gl.Disable(EnableCap.Multisample);
            gl.Disable((EnableCap)GLEnum.PrimitiveRestart);
            gl.LineWidth(1.0f);
            gl.PolygonOffset(0f, 0f);
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Disable((EnableCap)GLEnum.ProgramPointSize);
        }
    }
}
