#version 300 es

in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec3 vertexNormal;

uniform mat4 mvp;
uniform mat4 matModel;
uniform mat4 matNormal;

out vec3 fragPosition;
out vec2 fragTexCoord;
out vec3 fragNormal;

uniform vec2 textureScale;

void main()
{
    fragPosition = vec3(matModel * vec4(vertexPosition, 1.0));
    fragTexCoord = vertexTexCoord * textureScale;
    fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
    
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}