#version 430 core
#extension GL_ARB_bindless_texture : require
#extension GL_NV_gpu_shader5 : enable
#extension GL_ARB_gpu_shader_int64 : enable

precision highp float;
precision highp int;

in vec3 Normal;
in vec2 TexCoord;
in flat uvec2 TextureHandle;
in vec3 LightingColor;

uniform int uRenderPass;
uniform vec4 uHighlightColor;

out vec4 FragColor;

void main() {
    sampler2D tex = sampler2D(TextureHandle);
    vec4 color = texture(tex, TexCoord);
    
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
