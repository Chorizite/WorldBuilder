#version 300 es
out vec4 FragColor;

in vec4 vertexColor;

void main()
{
    FragColor = vertexColor;
}