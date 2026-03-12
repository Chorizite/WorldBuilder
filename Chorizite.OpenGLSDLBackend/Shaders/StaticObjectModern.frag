#version 430 core
#extension GL_ARB_bindless_texture : require
#extension GL_NV_gpu_shader5 : enable
#extension GL_ARB_gpu_shader_int64 : enable

in vec3 Normal;
in vec2 TexCoord;
in flat uvec2 TextureHandle;
in flat uint TextureIndex;
in vec3 LightingColor;

uniform int uRenderPass;
uniform vec4 uHighlightColor;

out vec4 FragColor;

void main() {
    sampler2DArray tex = sampler2DArray(TextureHandle);
    vec4 color = texture(tex, vec3(TexCoord, float(TextureIndex)));

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
