#version 330 core

in vec2 TexCoord;
in float Opacity;
in float TextureIndex;

uniform sampler2DArray uTextureArray;

out vec4 FragColor;

void main() {
    vec4 color = texture(uTextureArray, vec3(TexCoord, TextureIndex));
    color.a *= Opacity;
    
    if (color.a < 0.01) discard;
    
    FragColor = color;
}
