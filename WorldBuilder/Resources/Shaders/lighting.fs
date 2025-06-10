#version 300 es

precision highp float;
precision highp int;

in vec3 fragPosition;
in vec3 fragNormal;
in vec2 fragTexCoord;

out vec4 fragColor;

uniform sampler2D texture0;
uniform float metalness;
uniform float roughness;
uniform vec3 lightPos;
uniform vec3 viewPos;
uniform int debugMode;
uniform float time;

// Simple 2D noise function for normal perturbation
float noise(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// Function to perturb normals for liquid effect
vec3 perturbNormal(vec3 normal, vec2 uv, float strength) {
    vec2 offset = vec2(noise(uv + time * 0.1), noise(uv + vec2(5.2, 1.3) + time * 0.1)) * strength;
    vec3 tangent = normalize(cross(normal, vec3(0.0, 1.0, 0.0)));
    vec3 bitangent = normalize(cross(normal, tangent));
    vec3 perturbed = normal + tangent * offset.x + bitangent * offset.y;
    return normalize(perturbed);
}

void main()
{
    // Debug modes
    if (debugMode == 1) {
        vec3 normal = normalize(fragNormal);
        fragColor = vec4(abs(normal), 1.0);
        return;
    }
    if (debugMode == 2) {
        vec3 pos = 0.5 + 0.5 * normalize(fragPosition);
        fragColor = vec4(pos, 1.0);
        return;
    }

    // Dynamic texture coordinates for flow effect
    vec2 flowUV = fragTexCoord + vec2(sin(time * 0.2), cos(time * 0.15)) * 0.1;

    // Sample albedo texture
    vec3 albedo = texture(texture0, flowUV).rgb;

    // Perturb normals for liquid effect
    vec3 normal = perturbNormal(normalize(fragNormal), flowUV, 0.2);

    // Lighting calculations
    vec3 lightDir = normalize(lightPos - fragPosition);
    vec3 viewDir = normalize(viewPos - fragPosition);
    vec3 halfwayDir = normalize(lightDir + viewDir);

    // Fresnel effect
    float fresnel = pow(1.0 - max(dot(normal, viewDir), 0.0), 3.0) * 0.8 + 0.2;

    // Ambient
    vec3 ambient = 0.2 * albedo * (1.0 - metalness);

    // Diffuse
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * albedo * (1.0 - metalness);

    // Specular with enhanced shininess
    float shininess = exp2(18.0 * (1.0 - roughness)); // Higher shininess for liquid metal
    float spec = pow(max(dot(normal, halfwayDir), 0.0), shininess);
    vec3 specular = metalness * spec * vec3(1.0) * fresnel;

    // Combine lighting
    vec3 color = clamp(ambient + diffuse + specular, 0.0, 1.0);

    // Simple tone mapping
    color = color / (color + vec3(1.0));

    // Apply slight chromatic tint for liquid metal
    //color += vec3(0.05, 0.03, 0.1) * fresnel;

    fragColor = vec4(color, 1.0);
}