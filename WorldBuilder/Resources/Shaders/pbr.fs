#version 300 es

precision highp float;
precision highp int;

// Inputs from vertex shader
in vec3 fragPosition;
in vec2 fragTexCoord;
in vec3 fragNormal;

// Uniforms
uniform sampler2D texture0;    // Albedo
uniform sampler2D texture1;    // Metallic
uniform sampler2D texture2;    // Normal map (not used yet)
uniform sampler2D texture3;    // Roughness
uniform vec4 colDiffuse;
uniform vec3 lightPos;
uniform vec3 viewPos;

// Output
out vec4 finalColor;

void main()
{
    // Sample textures
    vec4 albedo = texture(texture0, fragTexCoord) * colDiffuse;
    float metallic = texture(texture1, fragTexCoord).r;
    float roughness = texture(texture3, fragTexCoord).r;

    // Get world-space normal (already transformed in vertex shader)
    vec3 normal = normalize(fragNormal);

    // Calculate light and view directions
    vec3 lightDir = normalize(lightPos - fragPosition);
    vec3 viewDir = normalize(viewPos - fragPosition);

    // Calculate distance-based attenuation
    float distance = length(lightPos - fragPosition);
    float attenuation = 1.0 / (1.0 + 0.7 * distance + 0.032 * distance * distance);

    // Light color with attenuation
    vec3 lightColor = vec3(3.0) * attenuation;

    // Diffuse lighting
    float NdotL = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = albedo.rgb * lightColor * NdotL;

    // Ambient lighting
    vec3 ambient = vec3(0.4) * albedo.rgb; // Reduced ambient for better contrast

    // Combine lighting
    vec3 finalLighting = diffuse + ambient;
    finalColor = vec4(finalLighting, albedo.a);
}