#version 330 core

uniform vec4 uOutlineColor;
uniform sampler2DArray uTextureArray;

in vec2 TexCoord;
in float TextureIndex;

out vec4 FragColor;

void main() {
    vec4 texColor = texture(uTextureArray, vec3(TexCoord, TextureIndex));
    if (texColor.a < 0.1) discard;
    
    FragColor = uOutlineColor;
}
