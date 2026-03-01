#version 330 core
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler2DArray;

uniform mat4 xView;
uniform mat4 xProjection;
uniform mat4 xWorld;
uniform vec3 xLightDirection;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in uvec4 inPackedBase;
layout(location = 3) in uvec4 inPackedOverlay0;
layout(location = 4) in uvec4 inPackedOverlay1;
layout(location = 5) in uvec4 inPackedOverlay2;
layout(location = 6) in uvec4 inPackedRoad0;
layout(location = 7) in uvec4 inPackedRoad1;

out vec3 vTexUV;
out vec4 vOverlay0;
out vec4 vOverlay1;
out vec4 vOverlay2;
out vec4 vRoad0;
out vec4 vRoad1;
out float vLightingFactor;
out vec2 vWorldPos;
out vec3 vNormal;

vec4 unpackTexCoord(uvec4 packedCoords) {
    // packed.x contains the UV bits in the low byte
    // packed.z contains texIdx
    // packed.w contains alphaIdx
    
    uint packedByte = packedCoords.x;
    uint packedU = (packedByte >> 4u) & 3u;  // Extract bits 4-5
    uint packedV = (packedByte >> 6u) & 3u;  // Extract bits 6-7
    
    // Convert 0,1,2 back to -1,0,1
    float u = float(packedU) - 1.0;
    float v = float(packedV) - 1.0;
    float texIdx = float(packedCoords.z);
    float alphaIdx = float(packedCoords.w);
    
    // Check if texture is unused (255 = -1 in shader space)
    if (texIdx >= 254.0) {
        texIdx = -1.0;
    }
    if (alphaIdx >= 254.0) {
        alphaIdx = -1.0;
    }
    
    return vec4(u, v, texIdx, alphaIdx);
}

void main() {
    gl_Position = xProjection * xView * xWorld * vec4(inPosition, 1.0);
    vWorldPos = inPosition.xy;
 
    vTexUV = unpackTexCoord(inPackedBase).xyz;
    
    // Unpack all compressed texture coordinates
    vOverlay0 = unpackTexCoord(inPackedOverlay0);
    vOverlay1 = unpackTexCoord(inPackedOverlay1);
    vOverlay2 = unpackTexCoord(inPackedOverlay2);
    vRoad0 = unpackTexCoord(inPackedRoad0);
    vRoad1 = unpackTexCoord(inPackedRoad1);
    
    vNormal = inNormal;
}