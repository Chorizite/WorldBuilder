#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in mat4 aInstanceMatrix;
layout(location = 7) in float aTextureIndex;

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

uniform float uOutlineWidth;

out vec2 TexCoord;
out float TextureIndex;

void main() {
    vec4 worldPos = aInstanceMatrix * vec4(aPosition, 1.0);
    vec3 worldNormal = normalize(mat3(aInstanceMatrix) * aNormal);
    
    gl_Position = uViewProjection * worldPos;
    vec4 clipNormal = uViewProjection * vec4(worldNormal, 0.0);
    
    if (uOutlineWidth > 0.0 && length(clipNormal.xy) > 0.0001) {
        vec2 viewportSize = max(uViewportSize, vec2(1.0));
        // Normalize the normal in screen space to ensure consistent thickness regardless of aspect ratio
        vec2 screenNormal = normalize(clipNormal.xy * viewportSize);
        vec2 offset = screenNormal * (uOutlineWidth * 2.0) / viewportSize;
        gl_Position.xy += offset * gl_Position.w;
    }
    
    TexCoord = aTexCoord;
    TextureIndex = aTextureIndex;
}
