#version 330 core

precision highp float;
precision highp int;
precision highp sampler2D;

uniform sampler2D _tex;
uniform bool useTexture; // Set this from your application code

in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;

void main() {
    vec4 texColor = texture(_tex, fragTexCoord);
    finalColor = useTexture ? fragColor * texColor : fragColor;
}