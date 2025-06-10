#version 300 es

precision highp float;
precision highp int;

in vec3 fragPosition;
in vec2 fragTexCoord;
in vec3 fragNormal;
uniform sampler2D texture0;    // albedo
uniform sampler2D texture1;    // metallic
uniform sampler2D texture2;    // normal
uniform sampler2D texture3;    // roughness
uniform vec4 colDiffuse;
uniform vec3 lightPos;
uniform vec3 viewPos;
out vec4 finalColor;

void main()
{
    vec4 albedo = texture(texture0, fragTexCoord) * colDiffuse;
    float metallic = texture(texture1, fragTexCoord).r;
    float roughness = texture(texture3, fragTexCoord).r;
    vec3 normal = normalize(fragNormal);
    
    // Calculate light direction and view direction
    vec3 lightDir = normalize(lightPos - fragPosition);
    vec3 viewDir = normalize(viewPos - fragPosition);
    
    // Calculate distance for attenuation
    float distance = length(lightPos - fragPosition);
    float attenuation = 1.0 / (1.0 + 0.09 * distance + 0.032 * distance * distance);
    
    // Apply attenuation to light color
    vec3 lightColor = vec3(3.0) * attenuation;
    
    // Calculate lighting
    float NdotL = max(dot(normal, lightDir), 0.0);
    vec3 diffuse = albedo.rgb * lightColor * NdotL;
    vec3 ambient = vec3(0.6) * albedo.rgb;
    
    vec3 finalLighting = ambient + diffuse;
    finalColor = vec4(finalLighting, albedo.a);
}