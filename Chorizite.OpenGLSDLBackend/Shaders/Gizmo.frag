#version 330 core
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
    vec2 uViewportSize;
    vec2 uPadding4;
};

uniform vec4 uBaseColor;
uniform int uIsPie;
uniform vec3 uPieCenter;
uniform vec3 uPieStartDir;
uniform vec3 uPieAxis;
uniform float uPieAngle;

void main() {
    vec3 normal = normalize(vNormal);
    vec3 viewDir = normalize(uCameraPosition - vFragPos);
    
    if (uIsPie == 1) {
        vec3 localDir = normalize(vFragPos - uPieCenter);
        // Project onto the plane defined by uPieAxis
        localDir = normalize(localDir - dot(localDir, uPieAxis) * uPieAxis);
        
        vec3 startDir = normalize(uPieStartDir);
        vec3 orthoDir = normalize(cross(uPieAxis, startDir));
        
        float x = dot(localDir, startDir);
        float y = dot(localDir, orthoDir);
        float angle = atan(y, x);
        
        // Atan2 returns [-PI, PI]. We want [0, 2PI] or [-2PI, 0] depending on sign of uPieAngle.
        if (uPieAngle >= 0.0) {
            if (angle < 0.0) angle += 6.28318530718;
            if (angle > uPieAngle) discard;
        } else {
            if (angle > 0.0) angle -= 6.28318530718;
            if (angle < uPieAngle) discard;
        }
    }
    
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
