#version 330 core

precision highp float;

layout (location = 0) in vec2 aQuadPos; // (0 to 1, -0.5 to 0.5)
layout (location = 1) in vec3 aStart;
layout (location = 2) in vec3 aEnd;
layout (location = 3) in vec4 aColor;
layout (location = 4) in float aThickness;

out vec4 vertexColor;

uniform mat4 uView;
uniform mat4 uProjection;
uniform vec2 uViewportSize;

void main() {
    // Transform start and end points to clip space
    vec4 clipStart = uProjection * uView * vec4(aStart, 1.0);
    vec4 clipEnd = uProjection * uView * vec4(aEnd, 1.0);

    // Simple culling if both are behind the camera (too conservative but safe)
    if (clipStart.w <= 0.0 && clipEnd.w <= 0.0) {
        gl_Position = vec4(0.0, 0.0, 0.0, 0.0);
        return;
    }

    // Perspective divide to get NDC
    vec2 ndcStart = clipStart.xy / max(clipStart.w, 0.0001);
    vec2 ndcEnd = clipEnd.xy / max(clipEnd.w, 0.0001);

    // Convert to screen space (pixels)
    vec2 screenStart = (ndcStart + 1.0) * 0.5 * uViewportSize;
    vec2 screenEnd = (ndcEnd + 1.0) * 0.5 * uViewportSize;

    // Calculate line direction and normal in screen space
    vec2 dir = screenEnd - screenStart;
    if (length(dir) < 0.0001) {
        dir = vec2(1.0, 0.0);
    }
    dir = normalize(dir);
    vec2 normal = vec2(-dir.y, dir.x);

    // Current point on the line axis (start or end)
    vec4 clipPos = mix(clipStart, clipEnd, aQuadPos.x);
    
    // Offset perpendicular to line in screen space
    // (aQuadPos.y is -0.5 to 0.5)
    vec2 offsetNdc = normal * (aQuadPos.y * aThickness) * (2.0 / uViewportSize);
    
    gl_Position = clipPos;
    gl_Position.xy += offsetNdc * clipPos.w;

    vertexColor = aColor;
}
