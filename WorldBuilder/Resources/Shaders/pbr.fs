#version 300 es

precision highp float;
precision highp int;

#define MAX_LIGHTS              4
#define LIGHT_DIRECTIONAL       0
#define LIGHT_POINT             1
#define PI 3.14159265358979323846

struct Light {
    int enabled;
    int type;
    vec3 position;
    vec3 target;
    vec4 color;
    float intensity;
};

// Input vertex attributes (from vertex shader)
in vec3 fragPosition;
in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragNormal;
in vec4 shadowPos;
in mat3 TBN;

// Output fragment color
out vec4 finalColor;

// Input uniform values
uniform int numOfLights;
uniform sampler2D albedoMap;
uniform sampler2D mraMap;
uniform sampler2D normalMap;
uniform sampler2D emissiveMap;

uniform vec2 tiling;
uniform vec2 offset;

uniform int useTexAlbedo;
uniform int useTexNormal;
uniform int useTexMRA;
uniform int useTexEmissive;

uniform vec4  albedoColor;
uniform vec4  emissiveColor;
uniform float normalValue;
uniform float metallicValue;
uniform float roughnessValue;
uniform float aoValue;
uniform float emissivePower;

// Input lighting values
uniform Light lights[MAX_LIGHTS];
uniform vec3 viewPos;

uniform vec3 ambientColor;
uniform float ambient;

// Reflectivity in range 0.0 to 1.0
vec3 SchlickFresnel(float hDotV, vec3 refl)
{
    return refl + (1.0 - refl) * pow(clamp(1.0 - hDotV, 0.0, 1.0), 5.0);
}

float GgxDistribution(float nDotH, float roughness)
{
    float a = roughness * roughness * roughness * roughness;
    float d = nDotH * nDotH * (a - 1.0) + 1.0;
    d = PI * d * d;
    return (a / max(d, 0.0000001));
}

float GeomSmith(float nDotV, float nDotL, float roughness)
{
    float r = roughness + 1.0;
    float k = r * r / 8.0;
    float ik = 1.0 - k;
    float ggx1 = nDotV / (nDotV * ik + k);
    float ggx2 = nDotL / (nDotL * ik + k);
    return ggx1 * ggx2;
}

vec3 ComputePBR()
{
    // Get albedo from texture or use base color
    vec3 albedo = vec3(1.0);
    if (useTexAlbedo == 1) {
        albedo = texture(albedoMap, vec2(fragTexCoord.x * tiling.x + offset.x, fragTexCoord.y * tiling.y + offset.y)).rgb;
    }
    // Apply albedo color tint
    albedo = albedo * albedoColor.rgb;
    
    // Get material properties
    float metallic = clamp(metallicValue, 0.0, 1.0);
    float roughness = clamp(roughnessValue, 0.04, 1.0); // Prevent roughness from being 0
    float ao = clamp(aoValue, 0.0, 1.0);
    
    // Apply MRA texture if enabled
    if (useTexMRA == 1) {
        vec4 mra = texture(mraMap, vec2(fragTexCoord.x * tiling.x + offset.x, fragTexCoord.y * tiling.y + offset.y));
        metallic = clamp(mra.r * metallicValue, 0.0, 1.0);
        roughness = clamp(mra.g * roughnessValue, 0.04, 1.0);
        ao = clamp(mra.b * aoValue, 0.0, 1.0);
    }

    // Get normal
    vec3 N = normalize(fragNormal);
    if (useTexNormal == 1) {
        vec3 normalTex = texture(normalMap, vec2(fragTexCoord.x * tiling.x + offset.x, fragTexCoord.y * tiling.y + offset.y)).rgb;
        normalTex = normalize(normalTex * 2.0 - 1.0);
        N = normalize(TBN * normalTex);
    }

    vec3 V = normalize(viewPos - fragPosition);

    // Get emissive
    vec3 emissive = vec3(0.0);
    if (useTexEmissive == 1) {
        emissive = texture(emissiveMap, vec2(fragTexCoord.x * tiling.x + offset.x, fragTexCoord.y * tiling.y + offset.y)).g 
                  * emissiveColor.rgb * emissivePower;
    }

    // Base reflectivity for dielectrics vs metals
    vec3 baseRefl = mix(vec3(0.04), albedo, metallic);
    vec3 lightAccum = vec3(0.0);

    // Calculate lighting
    for (int i = 0; i < numOfLights; i++) {
        if (lights[i].enabled == 0) continue;
        
        vec3 L = normalize(lights[i].position - fragPosition);
        vec3 H = normalize(V + L);
        float dist = length(lights[i].position - fragPosition);
        float attenuation = 1.0 / (1.0 + 0.02 * dist + 0.032 * dist * dist); // More realistic attenuation
        vec3 radiance = lights[i].color.rgb * lights[i].intensity * attenuation;

        // BRDF
        float nDotV = max(dot(N, V), 0.000001);
        float nDotL = max(dot(N, L), 0.000001);
        float hDotV = max(dot(H, V), 0.0);
        float nDotH = max(dot(N, H), 0.0);
        
        float D = GgxDistribution(nDotH, roughness);
        float G = GeomSmith(nDotV, nDotL, roughness);
        vec3 F = SchlickFresnel(hDotV, baseRefl);

        vec3 spec = (D * G * F) / (4.0 * nDotV * nDotL + 0.000001);
        
        vec3 kD = vec3(1.0) - F;
        kD *= 1.0 - metallic;
        
        lightAccum += (kD * albedo / PI + spec) * radiance * nDotL;
    }
    
    // *** FIXED AMBIENT CALCULATION ***
    vec3 ambientFinal = ambientColor * albedo * ambient;
    
    return ambientFinal + lightAccum * ao + emissive;
}

void main()
{
    vec3 color = ComputePBR();

    // HDR tonemapping (fixed)
    color = color / (color + vec3(1.0));
    
    // Gamma correction
    color = pow(color, vec3(1.0/2.2));

    finalColor = vec4(color, 1.0);
}