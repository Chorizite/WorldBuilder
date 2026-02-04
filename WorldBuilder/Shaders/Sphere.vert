#version 330 core
precision highp float;

layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec3 aInstancePosition;

uniform mat4 uViewProjection;
uniform float uSphereRadius;

out vec3 vNormal;
out vec3 vFragPos;

void main() {
    // Scale the sphere by radius and translate to instance position
    vec3 scaledPosition = aPosition * uSphereRadius + aInstancePosition;
    gl_Position = uViewProjection * vec4(scaledPosition, 1.0);
    vFragPos = scaledPosition;
    vNormal = normalize(aNormal);
}