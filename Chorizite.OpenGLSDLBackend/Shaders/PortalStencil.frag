#version 330 core
precision highp float;

out vec4 FragColor;

uniform int uWriteFarDepth;

void main() {
    if (uWriteFarDepth != 0) {
        // Write far depth to clear the depth buffer in the portal region.
        // This punches through the building exterior's depth so interior
        // geometry at any depth can be rendered through the stencil mask.
        gl_FragDepth = 1.0;
    } else {
        // Default depth is used if gl_FragDepth is not written.
        gl_FragDepth = gl_FragCoord.z;
    }

    // Color writes are suppressed via ColorMask(false) on the CPU side.
    // Output is required by GLSL but will not be written to the framebuffer.
    FragColor = vec4(0.0);
}
