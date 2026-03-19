#version 330 core

layout (location = 0) in vec3 aPosition; // Basic quad vertex (-0.5 to 0.5)
layout (location = 1) in vec2 aTexCoord;

// Instance attributes
layout (location = 2) in vec3 iPosition;
layout (location = 3) in vec3 iScaleOpacityActive; // x=Scale, y=Opacity, z=Active
layout (location = 4) in float iTextureIndex;
layout (location = 5) in vec4 iRotation; // Quaternion
layout (location = 6) in vec2 iSize;
layout (location = 7) in float iIsBillboard;

uniform mat4 uViewProjection;
uniform vec3 uCameraUp;
uniform vec3 uCameraRight;

out vec2 TexCoord;
out float Opacity;
out float TextureIndex;

vec3 rotate_vector(vec3 v, vec4 q) {
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

void main() {
    TexCoord = aTexCoord;
    Opacity = iScaleOpacityActive.y;
    TextureIndex = iTextureIndex;

    float scale = iScaleOpacityActive.x;
    vec3 worldPos;

    if (iIsBillboard > 0.5) {
        // Use cylindrical billboarding (upright) to match client's PointSpriteVS
        vec3 billboardUp = vec3(0.0, 0.0, 1.0);
        vec3 billboardRight = normalize(vec3(uCameraRight.x, uCameraRight.y, 0.0));
        
        // Fallback if looking straight down
        if (length(billboardRight) < 0.01) {
            billboardRight = uCameraRight;
        }

        worldPos = iPosition
            + billboardRight * aPosition.x * iSize.x * scale
            + billboardUp * aPosition.z * iSize.y * scale;
    } else {
        // Standard 3D rotation using quaternion
        vec3 localPos = vec3(aPosition.x * iSize.x * scale, 
                             0.0, 
                             aPosition.z * iSize.y * scale);
        
        worldPos = iPosition + rotate_vector(localPos, iRotation);
    }

    gl_Position = uViewProjection * vec4(worldPos, 1.0);
}
