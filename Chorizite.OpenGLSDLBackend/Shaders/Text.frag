#version 330 core
precision mediump float;

in vec4 v_color;
in vec2 v_texCoord;

out vec4 fragColor;

uniform sampler2D _tex;      // Bitmap font texture
uniform vec4 textColor;      // Text fill color
uniform vec4 strokeColor;    // Stroke color
uniform vec4 shadowColor;    // Shadow color
uniform vec2 shadowOffset;   // Shadow offset in texture space
uniform float strokeWidth;   // Stroke width (0.0 to disable)
uniform vec2 texelSize;      // Size of one texel (1.0 / textureSize)

void main() {
    // Sample bitmap texture
    float textAlpha = texture(_tex, v_texCoord).a;
    
    // Shadow
    float shadowAlpha = texture(_tex, v_texCoord + shadowOffset).a;
    
    // Stroke (sample only 8 neighboring texels for a simple outline)
    float strokeAlpha = 0.0;
    if (strokeWidth > 0.0) {
        // Define 8 directions for sampling (approximating a circular stroke)
        vec2 offsets[8] = vec2[](
            vec2(-1.0, -1.0), vec2(0.0, -1.0), vec2(1.0, -1.0),
            vec2(-1.0,  0.0),                  vec2(1.0,  0.0),
            vec2(-1.0,  1.0), vec2(0.0,  1.0), vec2(1.0,  1.0)
        );
        
        float maxAlpha = textAlpha;
        for (int i = 0; i < 8; i++) {
            float sampleAlpha = texture(_tex, v_texCoord + offsets[i] * texelSize * strokeWidth).a;
            maxAlpha = max(maxAlpha, sampleAlpha);
        }
        strokeAlpha = maxAlpha * (1.0 - textAlpha); // Stroke only where text is not
    }
    
    // Combine layers: shadow first, then stroke, then text
    vec4 color = vec4(0.0);
    color = mix(color, shadowColor, shadowAlpha * shadowColor.a);
    if (strokeWidth > 0.0) {
        color = mix(color, strokeColor, strokeAlpha * strokeColor.a * (1.0 - textAlpha));
    }
    color = mix(color, textColor * v_color, textAlpha * textColor.a);
    
    fragColor = color;
}