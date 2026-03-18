#version 330 core

layout (location = 0) in vec3 aPosition; // Basic quad vertex
layout (location = 1) in vec2 aTexCoord;

// Instance attributes
layout (location = 2) in vec3 iPosition;
layout (location = 3) in vec3 iScaleOpacityActive; // x=Scale, y=Opacity, z=Active
layout (location = 4) in float iTextureIndex;
layout (location = 5) in float iRotation;

uniform mat4 uViewProjection;
uniform vec3 uCameraUp;
uniform vec3 uCameraRight;

out vec2 TexCoord;
out float Opacity;
out float TextureIndex;

void main() {
    TexCoord = aTexCoord;
    Opacity = iScaleOpacityActive.y;
    TextureIndex = iTextureIndex;

    float scale = iScaleOpacityActive.x;
    float cosR = cos(iRotation);
    float sinR = sin(iRotation);

    // Cylindrical Billboarding (upright)
    // Most particles in AC stay upright (Z is up)
    vec3 billboardUp = vec3(0.0, 0.0, 1.0);
    vec3 billboardRight = normalize(vec3(uCameraRight.x, uCameraRight.y, 0.0));
    
    // Fallback if looking straight down
    if (length(billboardRight) < 0.01) {
        billboardRight = uCameraRight;
    }

    // Apply instance rotation around the view axis (approximate for cylindrical)
    vec3 rotatedRight = (billboardRight * cosR - billboardUp * sinR);
    vec3 rotatedUp = (billboardRight * sinR + billboardUp * cosR);

    // aPosition.x is horizontal (-0.5 to 0.5)
    // aPosition.z is vertical (0.0 to 1.0)
    vec3 worldPos = iPosition
        + rotatedRight * aPosition.x * scale
        + rotatedUp * aPosition.z * scale;

    gl_Position = uViewProjection * vec4(worldPos, 1.0);
}