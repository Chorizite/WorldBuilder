#version 330 core

precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler2DArray;

out vec4 FragColor;

in vec4 vertexColor;

void main() {
    FragColor = vertexColor;
}
