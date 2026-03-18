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
    
    int renderPass = uRenderPass & 0xFF;
    bool isAdditive = (uRenderPass & 0x100) != 0;

    if (renderPass == 0) {
        // Opaque pass - discard transparent pixels so they don't write to depth (Alpha Test)
        if (isAdditive || color.a < 0.95) discard;
    } else if (renderPass == 1) {
        // Transparent pass - discard pixels that were already drawn in the opaque pass
        if (!isAdditive) {
            if (color.a >= 0.95) discard;
            if (color.a < 0.05) discard; // Fix for massive Transparent pass overdraw: discard perfectly empty pixels
        }
    } else if (renderPass == 2) {
        // Single pass mode (or fallback)
        if (color.a < 0.1) discard;
    }
    
    color.rgb *= LightingColor;

    if (uHighlightColor.a > 0.0) {
        color.rgb = mix(color.rgb, uHighlightColor.rgb, uHighlightColor.a);
    }

    FragColor = color;
}