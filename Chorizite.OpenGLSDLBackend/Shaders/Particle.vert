#version 330 core

layout (location = 0) in vec3 aPosition; // Basic quad vertex (-0.5 to 0.5)
layout (location = 1) in vec2 aTexCoord;

// Instance attributes
layout (location = 2) in vec3 iPosition;
layout (location = 4) in float iTextureIndex;
layout (location = 3) in vec3 iScaleOpacityActive; // x=Scale, y=Opacity, z=Active
layout (location = 5) in float iRotation;
layout (location = 6) in vec2 iSize;

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
    vec3 billboardUp = vec3(0.0, 0.0, 1.0);
    vec3 billboardRight = normalize(vec3(uCameraRight.x, uCameraRight.y, 0.0));
    
    // Fallback if looking straight down
    if (length(billboardRight) < 0.01) {
        billboardRight = uCameraRight;
    }

    // Apply instance rotation around the view axis
    vec3 rotatedRight = (billboardRight * cosR - billboardUp * sinR);
    vec3 rotatedUp = (billboardRight * sinR + billboardUp * cosR);

    // Expansion logic matching ACViewer point sprite shader:
    // aPosition is -0.5 to 0.5 (centered)
    // width = iSize.x * scale (scale is baseScale 0.9 * p.scale)
    // height = iSize.y * scale
    vec3 worldPos = iPosition
        + rotatedRight * aPosition.x * iSize.x * scale
        + rotatedUp * aPosition.z * iSize.y * scale;

    gl_Position = uViewProjection * vec4(worldPos, 1.0);
}