#version 330 core

in vec3 Normal;
in vec2 TexCoord;
in float TextureIndex;
in vec3 LightingColor;

uniform sampler2DArray uTextureArray;
uniform int uRenderPass;
uniform vec4 uHighlightColor;

out vec4 FragColor;

void main() {
    vec4 color = texture(uTextureArray, vec3(TexCoord, TextureIndex));
    
    if (uRenderPass == 0) {
        // Opaque pass - discard transparent pixels so they don't write to depth (Alpha Test)
        if (color.a < 0.95) discard;
    } else if (uRenderPass == 1) {
        // Transparent pass - discard pixels that were already drawn in the opaque pass
        if (color.a >= 0.95) discard;
    } else if (uRenderPass == 2) {
        // Single pass mode (or fallback)
        if (color.a < 0.1) discard;
    }
    
    color.rgb *= LightingColor;

    if (uHighlightColor.a > 0.0) {
        color.rgb = mix(color.rgb, uHighlightColor.rgb, uHighlightColor.a);
    }

    FragColor = color;
}