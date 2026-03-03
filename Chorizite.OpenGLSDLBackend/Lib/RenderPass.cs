namespace Chorizite.OpenGLSDLBackend.Lib {
    /// <summary>
    /// Represents the different render passes in the graphics engine.
    /// </summary>
    public enum RenderPass {
        /// <summary>
        /// The opaque pass. Only non-transparent objects are rendered.
        /// </summary>
        Opaque = 0,

        /// <summary>
        /// The transparent pass. Only transparent objects are rendered, usually after the opaque pass.
        /// </summary>
        Transparent = 1,

        /// <summary>
        /// A single-pass render that includes both opaque and (sometimes) transparent objects,
        /// or for special cases like skyboxes and certain UI elements.
        /// </summary>
        SinglePass = 2
    }
}
