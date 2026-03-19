#version 330 core

in vec2 TexCoord;
in float Opacity;
in float TextureIndex;

uniform sampler2DArray uTextureArray;

out vec4 FragColor;

void main() {
    // Reverting to standard non-premultiplied sampling.
    vec4 color = texture(uTextureArray, vec3(TexCoord, TextureIndex));
    
    // Standard alpha blending: SrcAlpha, OneMinusSrcAlpha.
    color.a *= Opacity;
    
    // Alpha test to discard fully transparent pixels (standard AC behavior)
    if (color.a < 0.005) discard;
    
    FragColor = color * vec4(0.8, 0.8, 0.8, 1.0);
}