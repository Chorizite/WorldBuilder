#version 330 core
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler2DArray;

layout (std140) uniform SceneData {
    mat4 uView;
    mat4 uProjection;
    mat4 uViewProjection;
    vec3 uCameraPosition;
    vec3 uLightDirection;
    vec3 uSunlightColor;
    vec3 uAmbientColor;
    float uSpecularPower;
};

uniform mat4 xWorld;
uniform float uTexTiling[36];

layout(location = 0) in vec3 inPosition;
layout(location = 1) in uvec4 inPacked0;
layout(location = 2) in uvec4 inPacked1;
layout(location = 3) in uvec4 inPacked2;
layout(location = 4) in uvec4 inPacked3;

out vec2 vBaseUV;
out float vBaseTexIdx;
out vec4 vOverlay0;
out vec4 vOverlay1;
out vec4 vOverlay2;
out vec4 vRoad0;
out vec4 vRoad1;
out float vLightingFactor;
out vec2 vWorldPos;
out vec3 vWorldPos3D;

vec4 unpackOverlayLayer(uint texIdxU, uint alphaIdxU, uint rotIdx, vec2 baseUV) {
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
    gl_Position = uViewProjection * xWorld * vec4(inPosition, 1.0);
    vWorldPos = inPosition.xy;
    vWorldPos3D = (xWorld * vec4(inPosition, 1.0)).xyz;
 
    uint rotBase = inPacked3.x & 3u; // Note: rotBase is currently unused but kept for parity
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
    
    vBaseUV = baseUV;
    vBaseTexIdx = float(inPacked0.x);
    if (vBaseTexIdx >= 254.0) vBaseTexIdx = -1.0;

    vOverlay0 = unpackOverlayLayer(inPacked0.z, inPacked0.w, rotOvl0, baseUV);
    vOverlay1 = unpackOverlayLayer(inPacked1.x, inPacked1.y, rotOvl1, baseUV);
    vOverlay2 = unpackOverlayLayer(inPacked1.z, inPacked1.w, rotOvl2, baseUV);
    vRoad0 = unpackOverlayLayer(inPacked2.x, inPacked2.y, rotRd0, baseUV);
    vRoad1 = unpackOverlayLayer(inPacked2.z, inPacked2.w, rotRd1, baseUV);
}
