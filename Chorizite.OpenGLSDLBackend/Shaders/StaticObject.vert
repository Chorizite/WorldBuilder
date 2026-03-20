#version 330 core

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in mat4 aInstanceMatrix;
layout(location = 7) in float aTextureIndex;
layout(location = 8) in uint aCellId;

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

uniform int uFilterByCell;
uniform int uActiveCellCount;
uniform uint uActiveCells[256];

out vec3 Normal;
out vec2 TexCoord;
out float TextureIndex;
out vec3 LightingColor;

void main() {
    if (uFilterByCell == 1) {
        bool isVisible = false;
        for (int i = 0; i < uActiveCellCount; i++) {
            if (uActiveCells[i] == aCellId) {
                isVisible = true;
                break;
            }
        }
        if (!isVisible) {
            gl_Position = vec4(0.0);
            return;
        }
    }

    vec4 worldPos = aInstanceMatrix * vec4(aPosition, 1.0);
    gl_Position = uViewProjection * worldPos;
    Normal = normalize(mat3(aInstanceMatrix) * aNormal);
    TexCoord = aTexCoord;
    TextureIndex = aTextureIndex;
    
    float diff = max(dot(Normal, normalize(uLightDirection)), 0.0);
    LightingColor = clamp(uAmbientColor + uSunlightColor * diff + 0.15, 0.0, 1.0);
}
