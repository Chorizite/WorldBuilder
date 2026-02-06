#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec3 aNormal;
layout (location = 2) in vec4 aTexCoord0;

out vec3 FragPos;
out vec3 Normal;
out vec4 TexCoord0;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main() {
    FragPos = vec3(uModel * vec4(aPosition, 1.0));
    Normal = mat3(transpose(inverse(uModel))) * aNormal;
    TexCoord0 = aTexCoord0;
    
    gl_Position = uProjection * uView * vec4(FragPos, 1.0);
}
