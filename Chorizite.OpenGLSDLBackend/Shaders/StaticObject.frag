#version 330 core
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler2DArray;

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
        // Opaque pass
        if (color.a < 0.95) discard;
    } else if (uRenderPass == 1) {
        // Transparent pass
        if (color.a > 0.95) discard;
    } else {
        // Single pass mode (or fallback)
        if (color.a < 0.5) discard;
    }
    
    color.rgb *= LightingColor;

    if (uHighlightColor.a > 0.0) {
        color.rgb = mix(color.rgb, uHighlightColor.rgb, uHighlightColor.a);
    }

    FragColor = color;
}