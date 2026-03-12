#version 430 core
#extension GL_ARB_bindless_texture : require
#extension GL_NV_gpu_shader5 : enable
#extension GL_ARB_gpu_shader_int64 : enable
#extension GL_ARB_shader_draw_parameters : require

precision highp float;
precision highp int;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;

struct ModernInstanceData {
    mat4 Transform;
    uint CellId;
};

struct ModernBatchData {
    uvec2 TextureHandle;
    uint TextureIndex;
    uint Padding;
};

layout(std430, binding = 0) readonly buffer InstanceBuffer {
    ModernInstanceData Instances[];
};

layout(std430, binding = 1) readonly buffer BatchBuffer {
    ModernBatchData Batches[];
};

layout (std140) uniform SceneData {
    mat4 uView;
    mat4 uProjection;
    mat4 uViewProjection;
    vec3 uCameraPosition;
    vec3 uLightDirection;
    vec3 uSunlightColor;
    vec3 uAmbientColor;
    float uSpecularPower;
    vec2 uViewportSize;
    vec2 uPadding4;
};

uniform int uDrawIDOffset;
uniform int uFilterByCell;
uniform int uActiveCellCount;
uniform uint uActiveCells[256];

out vec3 Normal;
out vec2 TexCoord;
out flat uvec2 TextureHandle;
out flat uint TextureIndex;
out vec3 LightingColor;

void main() {
    int instanceIndex = gl_BaseInstanceARB + gl_InstanceID;
    ModernInstanceData inst = Instances[instanceIndex];

    if (uFilterByCell == 1) {
        bool isVisible = false;
        for (int i = 0; i < uActiveCellCount; i++) {
            if (uActiveCells[i] == inst.CellId) {
                isVisible = true;
                break;
            }
        }
        if (!isVisible) {
            gl_Position = vec4(0.0);
            return;
        }
    }

    vec4 worldPos = inst.Transform * vec4(aPosition, 1.0);
    gl_Position = uViewProjection * worldPos;
    Normal = normalize(mat3(inst.Transform) * aNormal);
    TexCoord = aTexCoord;
    TextureHandle = Batches[gl_DrawIDARB + uDrawIDOffset].TextureHandle;
    TextureIndex = Batches[gl_DrawIDARB + uDrawIDOffset].TextureIndex;
    
    float diff = max(dot(Normal, normalize(uLightDirection)), 0.0);
    LightingColor = clamp(uAmbientColor + uSunlightColor * diff, 0.0, 1.0);
}
