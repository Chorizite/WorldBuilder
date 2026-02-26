#version 300 es
precision highp float;

layout(location = 0) in vec3 aPosition;

uniform mat4 uViewProjection;

void main() {
    gl_Position = uViewProjection * vec4(aPosition, 1.0);
}
