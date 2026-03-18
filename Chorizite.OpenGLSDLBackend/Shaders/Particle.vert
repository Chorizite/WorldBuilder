#version 330 core

layout (location = 0) in vec3 aPosition; // Basic quad vertex (-0.5 to 0.5)
layout (location = 1) in vec2 aTexCoord;

// Instance attributes
layout (location = 2) in vec3 iPosition;
layout (location = 3) in vec3 iScaleOpacityActive; // x=scale, y=opacity, z=active (1.0 or 0.0)
layout (location = 4) in float iTextureIndex;

out vec2 TexCoord;
out float Opacity;
out float TextureIndex;

uniform mat4 uViewProjection;
uniform vec3 uCameraUp;
uniform vec3 uCameraRight;

void main() {
    if (iScaleOpacityActive.z < 0.5) {
        gl_Position = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    TexCoord = aTexCoord;
    Opacity = iScaleOpacityActive.y;
    TextureIndex = iTextureIndex;

    // Billboarding logic:
    // Use the explicitly passed right and up vectors
    vec3 cameraRight = uCameraRight;
    vec3 cameraUp = uCameraUp;

    vec3 worldPos = iPosition
        + cameraRight * aPosition.x * iScaleOpacityActive.x
        + cameraUp * aPosition.z * iScaleOpacityActive.x; // Use Z for vertical in AC world

    gl_Position = uViewProjection * vec4(worldPos, 1.0);
}
