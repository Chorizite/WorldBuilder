#version 330 core

precision highp float;
precision highp int;

out vec4 FragColor;

in vec3 vNormal;
in vec3 vFragPos;

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

uniform vec4 uBaseColor;

void main() {
    vec3 normal = normalize(vNormal);
    
    // We want the gizmo to always look somewhat lit from the camera's perspective
    // so it doesn't get lost in shadow if the scene light is dark.
    vec3 viewDir = normalize(uCameraPosition - vFragPos);
    
    // A slightly offset light direction looks good for 3D primitives
    vec3 lightDir = normalize(viewDir + vec3(0.2, 0.5, 0.0));
    
    float diff = max(dot(normal, lightDir), 0.0);
    
    // Add some rim lighting for edge definition
    float rim = 1.0 - max(dot(viewDir, normal), 0.0);
    rim = smoothstep(0.6, 1.0, rim);

    vec3 ambient = 0.5 * uBaseColor.rgb;
    vec3 diffuse = diff * 0.5 * uBaseColor.rgb;
    vec3 rimLight = vec3(rim) * 0.3 * uBaseColor.rgb;
    
    FragColor = vec4(ambient + diffuse + rimLight, uBaseColor.a);
}
