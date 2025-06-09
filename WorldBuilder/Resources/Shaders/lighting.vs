#version 330

in vec3 vertexPosition;
in vec3 vertexNormal;
in vec2 vertexTexCoord;

out vec3 fragPosition;
out vec3 fragNormal;
out vec2 fragTexCoord;

uniform mat4 mvp;
uniform mat4 model;
uniform int useNormalFix; // Added to toggle fix

void main()
{
    fragPosition = vec3(model * vec4(vertexPosition, 1.0));
        fragNormal = normalize(vec3(vertexNormal.x, vertexNormal.y, 0.1)); // Add Z-component
    fragTexCoord = vertexTexCoord;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}