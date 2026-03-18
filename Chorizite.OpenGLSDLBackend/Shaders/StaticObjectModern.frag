#version 430 core
#extension GL_ARB_bindless_texture : require
#extension GL_NV_gpu_shader5 : enable
#extension GL_ARB_gpu_shader_int64 : enable

in vec3 Normal;
in vec2 TexCoord;
in flat uvec2 TextureHandle;
in flat uint TextureIndex;
in vec3 LightingColor;
in flat uint Flags;

uniform int uRenderPass;
uniform vec4 uHighlightColor;

out vec4 FragColor;

void main() {
    sampler2DArray tex = sampler2DArray(TextureHandle);
    vec4 color = texture(tex, vec3(TexCoord, float(TextureIndex)));

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

    if ((Flags & 1u) != 0u) {
        color.rgb = mix(color.rgb, vec3(1.0, 0.0, 0.0), 0.5);
    }

    FragColor = color;
}
