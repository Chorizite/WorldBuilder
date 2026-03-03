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
layout(location = 1) in uvec4 inPacked0;
layout(location = 2) in uvec4 inPacked1;
layout(location = 3) in uvec4 inPacked2;
layout(location = 4) in uvec4 inPacked3;

out vec3 vTexUV;
out vec4 vOverlay0;
out vec4 vOverlay1;
out vec4 vOverlay2;
out vec4 vRoad0;
out vec4 vRoad1;
out float vLightingFactor;
out vec2 vWorldPos;
out vec3 vWorldPos3D;

vec4 unpackLayer(uint texIdxU, uint alphaIdxU, uint rotIdx, vec2 baseUV) {
    float texIdx = float(texIdxU);
    float alphaIdx = float(alphaIdxU);
    if (texIdx >= 254.0) texIdx = -1.0;
    if (alphaIdx >= 254.0) alphaIdx = -1.0;
    
    vec2 rotatedUV = baseUV;
    if (rotIdx == 1u) rotatedUV = vec2(1.0 - baseUV.y, baseUV.x);
    else if (rotIdx == 2u) rotatedUV = vec2(1.0 - baseUV.x, 1.0 - baseUV.y);
    else if (rotIdx == 3u) rotatedUV = vec2(baseUV.y, 1.0 - baseUV.x);
    
    return vec4(rotatedUV.x, rotatedUV.y, texIdx, alphaIdx);
}

void main() {
    gl_Position = xProjection * xView * xWorld * vec4(inPosition, 1.0);
    vWorldPos = inPosition.xy;
    vWorldPos3D = (xWorld * vec4(inPosition, 1.0)).xyz;
 
    uint rotBase = inPacked3.x & 3u;
    uint rotOvl0 = (inPacked3.x >> 2u) & 3u;
    uint rotOvl1 = (inPacked3.x >> 4u) & 3u;
    uint rotOvl2 = (inPacked3.x >> 6u) & 3u;
    uint rotRd0  = inPacked3.y & 3u;
    uint rotRd1  = (inPacked3.y >> 2u) & 3u;
    uint splitDir = (inPacked3.y >> 4u) & 1u;
    
    int vIdx = gl_VertexID % 6;
    int corner = 0;
    
    if (splitDir == 0u) {
        if (vIdx == 0) corner = 0;
        else if (vIdx == 1) corner = 3;
        else if (vIdx == 2) corner = 1;
        else if (vIdx == 3) corner = 1;
        else if (vIdx == 4) corner = 3;
        else if (vIdx == 5) corner = 2;
    } else {
        if (vIdx == 0) corner = 0;
        else if (vIdx == 1) corner = 2;
        else if (vIdx == 2) corner = 1;
        else if (vIdx == 3) corner = 0;
        else if (vIdx == 4) corner = 3;
        else if (vIdx == 5) corner = 2;
    }
    
    vec2 baseUV = vec2(0.0);
    if (corner == 0) baseUV = vec2(0.0, 1.0);
    else if (corner == 1) baseUV = vec2(1.0, 1.0);
    else if (corner == 2) baseUV = vec2(1.0, 0.0);
    else if (corner == 3) baseUV = vec2(0.0, 0.0);
    
    vTexUV = unpackLayer(inPacked0.x, inPacked0.y, rotBase, baseUV).xyz;
    vOverlay0 = unpackLayer(inPacked0.z, inPacked0.w, rotOvl0, baseUV);
    vOverlay1 = unpackLayer(inPacked1.x, inPacked1.y, rotOvl1, baseUV);
    vOverlay2 = unpackLayer(inPacked1.z, inPacked1.w, rotOvl2, baseUV);
    vRoad0 = unpackLayer(inPacked2.x, inPacked2.y, rotRd0, baseUV);
    vRoad1 = unpackLayer(inPacked2.z, inPacked2.w, rotRd1, baseUV);
}