#version 330 core

uniform mat4 xWorld;
uniform mat4 xProjection;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec4 inColor0;
layout(location = 2) in vec2 inTexCoord0;

out vec4 v_color;
out vec2 v_texCoord;

void main() {
   gl_Position = xProjection * xWorld * vec4(inPosition, 1.0); 
   v_color = inColor0;
   v_texCoord = inTexCoord0;
}