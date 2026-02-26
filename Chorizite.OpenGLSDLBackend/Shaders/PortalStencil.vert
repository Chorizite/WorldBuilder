#version 300 es
precision highp float;

layout(location = 0) in vec3 aPosition;

uniform mat4 uViewProjection;

void main() {
    vec4 pos = uViewProjection * vec4(aPosition, 1.0);
    // Clamp to near plane to prevent portal geometry from being clipped
    // when the camera is very close to or passing through a portal.
    // This ensures the stencil mask is still marked.
    // TODO: this is still a bit wonky, especially when approaching portals at steep angles.
    // A possible solution would be to use a custom clipping shader that clips against the portal plane instead of the near plane.
    if (pos.z < -pos.w) {
        pos.z = -pos.w;
    }
    gl_Position = pos;
}
