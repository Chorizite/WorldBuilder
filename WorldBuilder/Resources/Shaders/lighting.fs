#version 330

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

void main()
{
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

    vec3 albedo = texture(texture0, fragTexCoord).rgb;
    vec3 normal = normalize(fragNormal);
    vec3 lightDir = normalize(lightPos - fragPosition);
    vec3 viewDir = normalize(viewPos - fragPosition);
    
    // Ambient
    vec3 ambient = 0.3 * albedo;
    
    // Diffuse
    float diff = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = diff * albedo * 2.0;
    
    // Specular
    vec3 halfwayDir = normalize(lightDir + viewDir);
    float spec = pow(max(dot(normal, halfwayDir), 0.0), 64.0 * (1.0 - roughness));
    vec3 specular = metalness * spec * vec3(3.0);
    
    // Combine lighting
    vec3 color = ambient + diffuse + specular;
    fragColor = vec4(color, 1.0);
}