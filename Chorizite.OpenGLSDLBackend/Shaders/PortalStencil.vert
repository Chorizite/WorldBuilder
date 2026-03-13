#version 330 core

layout(location = 0) in vec3 aPosition;

layout (std140) uniform SceneData {
    mat4 uView;
    mat4 uProjection;
    mat4 uViewProjection;
    vec3 uCameraPosition;
    vec3 uLightDirection;
    vec3 uSunlightColor;
    vec3 uAmbientColor;
    float uSpecularPower;
    vec2 uViewportSize;
    vec2 uPadding4;
};

void main() {
    vec4 pos = uViewProjection * vec4(aPosition, 1.0);
    
    // Prevent division by zero and near-zero clipping issues when the camera 
    // is perfectly coplanar with the portal polygon.
    if (abs(pos.w) < 0.001) {
        pos.w = pos.w < 0.0 ? -0.001 : 0.001;
    }
    
    gl_Position = pos;
}
