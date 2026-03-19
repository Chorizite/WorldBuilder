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
        // Use cylindrical billboarding (upright)
        // Extract 2D rotation from quaternion (assuming only Z rotation for particles)
        // Or we could just use a separate iZRotation float for billboards.
        // For now, let's assume billboard means always camera facing + rotation around view axis.
        
        // This is a simplified 2D rotation from the quaternion's Z-axis component for billboards
        float cosR = 1.0 - 2.0 * (iRotation.y * iRotation.y + iRotation.z * iRotation.z);
        float sinR = 2.0 * (iRotation.x * iRotation.y + iRotation.w * iRotation.z);
        // The above is complex. Let's just use the quaternion to rotate billboardRight/Up
        
        vec3 billboardUp = vec3(0.0, 0.0, 1.0);
        vec3 billboardRight = normalize(vec3(uCameraRight.x, uCameraRight.y, 0.0));
        
        // Fallback if looking straight down
        if (length(billboardRight) < 0.01) {
            billboardRight = uCameraRight;
        }

        // Apply instance rotation around the view axis (roughly)
        // Since it's a billboard, we'll just treat the quaternion as a rotation in the billboard plane if possible,
        // but typically particles just use a float. We'll use the quaternion's Z rotation.
        float angle = 2.0 * acos(iRotation.w);
        float cosA = cos(angle);
        float sinA = sin(angle);
        
        vec3 rotatedRight = (billboardRight * cosA - billboardUp * sinA);
        vec3 rotatedUp = (billboardRight * sinA + billboardUp * cosA);

        worldPos = iPosition
            + rotatedRight * aPosition.x * iSize.x * scale
            + rotatedUp * aPosition.z * iSize.y * scale;
    } else {
        // Standard 3D rotation using quaternion
        vec3 localPos = vec3(aPosition.x * iSize.x * scale, 
                             0.0, 
                             aPosition.z * iSize.y * scale);
        
        worldPos = iPosition + rotate_vector(localPos, iRotation);
    }

    gl_Position = uViewProjection * vec4(worldPos, 1.0);
}
