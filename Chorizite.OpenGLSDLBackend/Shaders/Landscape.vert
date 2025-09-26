#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler2DArray;

uniform mat4 xViewProjection;
uniform mat4 xWorld;
uniform vec3 xLightDirection;

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec3 inTextureCoord;
layout(location = 3) in vec4 inOverlay0;
layout(location = 4) in vec4 inOverlay1;
layout(location = 5) in vec4 inOverlay2;
layout(location = 6) in vec4 inRoad0;
layout(location = 7) in vec4 inRoad1;

out vec3 vTexUV;
out vec4 vOverlay0;
out vec4 vOverlay1;
out vec4 vOverlay2;
out vec4 vRoad0;
out vec4 vRoad1;
out float vLightingFactor;
out vec2 vWorldPos;

void main() {
    gl_Position = xViewProjection * xWorld * vec4(inPosition, 1.0);
    vWorldPos = inPosition.xy;
 
    vTexUV = inTextureCoord;
    vOverlay0 = inOverlay0;
    vOverlay1 = inOverlay1;
    vOverlay2 = inOverlay2;
    vRoad0 = inRoad0;
    vRoad1 = inRoad1;
    vLightingFactor = 1.0;
}