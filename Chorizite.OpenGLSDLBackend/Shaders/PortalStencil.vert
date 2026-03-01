#version 330 core
precision highp float;

layout(location = 0) in vec3 aPosition;

uniform mat4 uViewProjection;

void main() {
    vec4 pos = uViewProjection * vec4(aPosition, 1.0);
    
    // Prevent division by zero and near-zero clipping issues when the camera 
    // is perfectly coplanar with the portal polygon.
    if (abs(pos.w) < 0.001) {
        pos.w = pos.w < 0.0 ? -0.001 : 0.001;
    }
    
    gl_Position = pos;
}
