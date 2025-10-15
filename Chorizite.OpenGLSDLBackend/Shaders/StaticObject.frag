#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler2DArray;

in vec3 Normal;
in vec2 TexCoord;
in float TextureIndex;
in float LightingFactor;

uniform sampler2DArray uTextureArray;

out vec4 FragColor;

void main() {
    vec4 color = texture(uTextureArray, vec3(TexCoord, TextureIndex));
    if (color.a < 0.5) discard; // Handle transparency
    color.rgb *= LightingFactor;
    FragColor = color;
}