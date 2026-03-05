#version 330 core

precision highp float;
precision highp int;

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;

out vec3 vNormal;
out vec3 vFragPos;

layout (std140) uniform SceneData {
    mat4 uView;
    mat4 uProjection;
    mat4 uViewProjection;
    vec3 uCameraPosition;
    vec3 uLightDirection;
    vec3 uSunlightColor;
    vec3 uAmbientColor;
    float uSpecularPower;
};

uniform mat4 uModel;

void main() {
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    gl_Position = uProjection * uView * worldPos;
    vFragPos = worldPos.xyz;
    vNormal = mat3(transpose(inverse(uModel))) * aNormal;
}
