#version 300 es
precision highp float;

out vec4 FragColor;

void main() {
    // Write far depth to clear the depth buffer in the portal region.
    // This punches through the building exterior's depth so interior
    // geometry at any depth can be rendered through the stencil mask.
    gl_FragDepth = 1.0;

    // Color writes are suppressed via ColorMask(false) on the CPU side.
    // Output is required by GLSL but will not be written to the framebuffer.
    FragColor = vec4(0.0);
}
