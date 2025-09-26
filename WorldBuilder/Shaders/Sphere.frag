#version 300 es
precision highp float;

in vec3 vNormal;
in vec3 vFragPos;

uniform vec3 uCameraPosition;
uniform vec3 uSphereColor;
uniform vec3 uLightDirection;
uniform float uAmbientIntensity;
uniform float uSpecularPower;
uniform vec3 uGlowColor;
uniform float uGlowIntensity;
uniform float uGlowPower;

out vec4 FragColor;

void main() {
    vec3 normal = normalize(vNormal);
    vec3 lightDir = normalize(uLightDirection);
    vec3 viewDir = normalize(uCameraPosition - vFragPos);

    // Diffuse lighting
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * uSphereColor;

    // Specular lighting
    vec3 reflectDir = reflect(-lightDir, normal);
    float spec = pow(max(dot(viewDir, reflectDir), 0.0), uSpecularPower);
    vec3 specular = spec * uSphereColor;

    // Ambient lighting
    vec3 ambient = uAmbientIntensity * uSphereColor;

    // Glow effect
    vec3 glow = uGlowIntensity * uGlowColor * pow(max(dot(normal, viewDir), 0.0), uGlowPower);

    // Combine lighting components
    vec3 result = ambient + diffuse + specular + glow;
    FragColor = vec4(result, 1.0);
}