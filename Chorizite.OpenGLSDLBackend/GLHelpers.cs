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
        public static void CheckErrors(bool logErrors = false, [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) {
            var error = Device?.GL.GetError();
            if (error.HasValue && error.Value != GLEnum.NoError) {
                if (!_loggedVersion && Device != null) {
                    _loggedVersion = true;
                    var version = Device.GL.GetStringS(GLEnum.Version);
                    var vendor = Device.GL.GetStringS(GLEnum.Vendor);
                    var renderer = Device.GL.GetStringS(GLEnum.Renderer);
                    Logger?.LogInformation($"GL Version: {version}, Vendor: {vendor}, Renderer: {renderer}");
                }
                string errorDetails = GetErrorDetails(error.Value);
                string location = $"{System.IO.Path.GetFileName(callerFile)}::{callerName}:{callerLine}";

                var program = Device?.GL.GetInteger(GLEnum.CurrentProgram);
                var vao = Device?.GL.GetInteger(GLEnum.VertexArrayBinding);
                var activeTex = Device?.GL.GetInteger(GLEnum.ActiveTexture);

                string message = $"OpenGL Error: {error} ({errorDetails}) at {location}. Program: {program}, VAO: {vao}, ActiveTex: {activeTex}";

                Logger?.LogError(message);
                throw new Exception(message);
            }
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckErrors(bool logErrors = false, string callerName = "",
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
        public static void CheckErrorsWithContext(string context, [CallerMemberName] string callerName = "",
            [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) {
            var error = Device?.GL.GetError();
            if (error.HasValue && error.Value != GLEnum.NoError) {
                string errorDetails = GetErrorDetails(error.Value);
                string location = $"{System.IO.Path.GetFileName(callerFile)}::{callerName}:{callerLine}";
                string message = $"OpenGL Error: {error} ({errorDetails})\nContext: {context}\nLocation: {location}";

                Logger?.LogError(message);
                throw new Exception(message);
            }
        }
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CheckErrorsWithContext(string context, string callerName = "",
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

                // Check if texture is immutable
                //gl.GetTexParameter(target, GetTextureParameter.TextureImmutableFormat, out int immutable);
                //info.AppendLine($"  Immutable: {immutable != 0}");

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
                uint boundTexture = (uint)gl.GetInteger(GetPName.TextureBinding2DArray);
                if (boundTexture == 0) {
                    errorMessage = "No texture is currently bound";
                    return false;
                }

                gl.GetTextureLevelParameter(boundTexture, 0, GetTextureParameter.TextureWidth, out int width);
                gl.GetTextureLevelParameter(boundTexture, 0, GetTextureParameter.TextureHeight, out int height);
                gl.GetTextureLevelParameter(boundTexture, 0, GetTextureParameter.TextureInternalFormat, out int format);

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

                //gl.GetTexParameter(target, GetTextureParameter.TextureImmutableFormat, out int immutable);
                //if (immutable == 0) {
                //    errorMessage = "Texture is mutable (not using glTexStorage), which can cause issues with mipmap generation";
                // Not necessarily fatal, but worth noting
                //}
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
    }
}