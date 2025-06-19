#version 320 es
precision highp float;

out vec4 outputColor;

uniform vec4 ourColor;

void main()
{
    outputColor = ourColor;
}