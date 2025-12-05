#version 330 core
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler2DArray;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in mat4 aInstanceMatrix;
layout(location = 7) in float aTextureIndex;

uniform mat4 uViewProjection;
uniform vec3 uCameraPosition;
uniform vec3 uLightDirection;
uniform float uAmbientIntensity;
uniform float uSpecularPower;

out vec3 Normal;
out vec2 TexCoord;
out float TextureIndex;
out float LightingFactor;

void main() {
    vec4 worldPos = aInstanceMatrix * vec4(aPosition, 1.0);
    gl_Position = uViewProjection * worldPos;
    Normal = normalize(mat3(aInstanceMatrix) * aNormal);
    TexCoord = aTexCoord;
    TextureIndex = aTextureIndex;
    LightingFactor = max(dot(Normal, -uLightDirection), 0.0) + uAmbientIntensity;
}