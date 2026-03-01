#version 330 core
precision highp float;

layout(location = 0) in vec3 aPosition;

uniform mat4 uViewProjection;

void main() {
    vec4 pos = uViewProjection * vec4(aPosition, 1.0);
    gl_Position = pos;
}
