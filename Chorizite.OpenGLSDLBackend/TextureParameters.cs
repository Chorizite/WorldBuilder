using Silk.NET.OpenGL;

namespace Chorizite.OpenGLSDLBackend {
    /// <summary>
    /// Configurable OpenGL texture parameters for wrap mode, filtering, mipmaps, and anisotropic filtering.
    /// </summary>
    public struct TextureParameters {
        public TextureWrapMode WrapS;
        public TextureWrapMode WrapT;
        public TextureMinFilter MinFilter;
        public TextureMagFilter MagFilter;
        public bool EnableMipmaps;
        public bool EnableAnisotropicFiltering;

        /// <summary>Standard tiling textures — Repeat + trilinear + aniso.</summary>
        public static readonly TextureParameters Default = new() {
            WrapS = TextureWrapMode.Repeat,
            WrapT = TextureWrapMode.Repeat,
            MinFilter = TextureMinFilter.LinearMipmapLinear,
            MagFilter = TextureMagFilter.Linear,
            EnableMipmaps = true,
            EnableAnisotropicFiltering = true,
        };

        /// <summary>Non-tiling textures (alpha maps, fonts, UI, object atlases) — ClampToEdge + trilinear + aniso.</summary>
        public static readonly TextureParameters ClampToEdge = new() {
            WrapS = TextureWrapMode.ClampToEdge,
            WrapT = TextureWrapMode.ClampToEdge,
            MinFilter = TextureMinFilter.LinearMipmapLinear,
            MagFilter = TextureMagFilter.Linear,
            EnableMipmaps = true,
            EnableAnisotropicFiltering = true,
        };
    }
}
