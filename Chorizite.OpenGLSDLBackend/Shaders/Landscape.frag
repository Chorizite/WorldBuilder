#version 300 es

precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler2DArray;

uniform sampler2DArray xOverlays;
uniform sampler2DArray xAlphas;
uniform float xAmbient;
uniform float uAlpha;

// Grid uniforms
uniform bool uShowLandblockGrid;  // Enable/disable landblock grid
uniform bool uShowCellGrid;       // Enable/disable cell grid
uniform vec3 uLandblockGridColor; // Color for landblock grid lines (RGB)
uniform vec3 uCellGridColor;      // Color for cell grid lines (RGB)
uniform float uGridLineWidth;     // Base width of grid lines in pixels
uniform float uGridOpacity;       // Opacity of grid lines (0.0 - 1.0)
uniform float uCameraDistance;    // Distance from camera to terrain
uniform float uScreenHeight;      // Screen height in pixels for scaling

in vec3 vTexUV;
in vec4 vOverlay0;
in vec4 vOverlay1;
in vec4 vOverlay2;
in vec4 vRoad0;
in vec4 vRoad1;
in float vLightingFactor;
in vec2 vWorldPos;

out vec4 FragColor;

vec4 maskBlend3(vec4 t0, vec4 t1, vec4 t2, float h0, float h1, float h2) {
    float a0 = h0 == 0.0 ? 1.0 : t0.a;
    float a1 = h1 == 0.0 ? 1.0 : t1.a;
    float a2 = h2 == 0.0 ? 1.0 : t2.a;
    float aR = 1.0 - (a0 * a1 * a2);
    a0 = 1.0 - a0;
    a1 = 1.0 - a1;
    a2 = 1.0 - a2;
    vec3 r0 = (a0 * t0.rgb + (1.0 - a0) * a1 * t1.rgb + (1.0 - a1) * a2 * t2.rgb);
    vec4 r;
    r.a = aR;
    r.rgb = (1.0 / aR) * r0;
    return r;
}

vec4 combineOverlays(vec3 pTexUV, vec4 pOverlay0, vec4 pOverlay1, vec4 pOverlay2) {
    float h0 = pOverlay0.z < 0.0 ? 0.0 : 1.0;
    float h1 = pOverlay1.z < 0.0 ? 0.0 : 1.0;
    float h2 = pOverlay2.z < 0.0 ? 0.0 : 1.0;
    vec4 overlay0 = vec4(0.0);
    vec4 overlay1 = vec4(0.0);
    vec4 overlay2 = vec4(0.0);
    vec4 overlayAlpha0 = vec4(0.0);
    vec4 overlayAlpha1 = vec4(0.0);
    vec4 overlayAlpha2 = vec4(0.0);
    vec2 uvb = pTexUV.xy;
    vec4 result = vec4(0.0);
    if (h0 > 0.0) {
        overlay0 = texture(xOverlays, vec3(uvb, pOverlay0.z));
        // Only sample alpha if alphaIdx is valid
        if (pOverlay0.w >= 0.0) {
            overlayAlpha0 = texture(xAlphas, pOverlay0.xyw);
            overlay0.a = overlayAlpha0.a;
        }
    }
    if (h1 > 0.0) {
        overlay1 = texture(xOverlays, vec3(uvb, pOverlay1.z));
        if (pOverlay1.w >= 0.0) {
            overlayAlpha1 = texture(xAlphas, pOverlay1.xyw);
            overlay1.a = overlayAlpha1.a;
        }
    }
    if (h2 > 0.0) {
        overlay2 = texture(xOverlays, vec3(uvb, pOverlay2.z));
        if (pOverlay2.w >= 0.0) {
            overlayAlpha2 = texture(xAlphas, pOverlay2.xyw);
            overlay2.a = overlayAlpha2.a;
        }
    }
    result = maskBlend3(overlay0, overlay1, overlay2, h0, h1, h2);
    return result;
}

vec4 combineRoad(vec3 pTexUV, vec4 pRoad0, vec4 pRoad1) {
    float h0 = pRoad0.z < 0.0 ? 0.0 : 1.0;
    float h1 = pRoad1.z < 0.0 ? 0.0 : 1.0;
    vec2 uvb = pTexUV.xy;
    vec4 result = vec4(0.0);
    if (h0 > 0.0) {
        result = texture(xOverlays, vec3(uvb, pRoad0.z));
        if (pRoad0.w >= 0.0) {
            vec4 roadAlpha0 = texture(xAlphas, pRoad0.xyw);
            result.a = 1.0 - roadAlpha0.a;
            if (h1 > 0.0 && pRoad1.w >= 0.0) {
                vec4 roadAlpha1 = texture(xAlphas, pRoad1.xyw);
                result.a = 1.0 - (roadAlpha0.a * roadAlpha1.a);
            }
        }
    }
    return result;
}

float saturate(float value) {
    return clamp(value, 0.0, 1.0);
}

vec3 saturate(vec3 value) {
    return clamp(value, 0.0, 1.0);
}

vec3 calculateGrid(vec2 worldPos, vec3 terrainColor) {
    // Early out if both grids are disabled
    if (!uShowLandblockGrid && !uShowCellGrid) {
        return vec3(0.0);
    }
    
    float lw = 192.0; // Landblock width
    float cw = 24.0;  // Cell width
    float glowWidthFactor = 1.5; // Glow extends wider than the line
    float glowIntensity = 0.5;   // Adjusted glow intensity
    float landblockLineWidthFactor = 2.0; // Double the thickness for landblock lines

    // Calculate pixel size in world units
    float worldUnitsPerPixel = uCameraDistance * tan(0.785398) * 2.0 / uScreenHeight; // Assuming 45-degree FOV
    float scaledLineWidth = uGridLineWidth * worldUnitsPerPixel;
    float scaledGlowWidth = scaledLineWidth * glowWidthFactor;
    float scaledLandblockGlowWidth = scaledGlowWidth * landblockLineWidthFactor; // Thicker glow for landblock lines

    // Determine if cell grid is visible
    bool showCellGrid = (cw / 2.0 > worldUnitsPerPixel);
    bool showLandblockGrid = (lw / 2.0 > worldUnitsPerPixel);

    // Use normal line width for landblock lines if cell grid is not visible
    float scaledLandblockLineWidth = showCellGrid ? scaledLineWidth * landblockLineWidthFactor : scaledLineWidth;

    if (!showLandblockGrid && !showCellGrid) {
        return vec3(0.0);
    }

    // Boost contrast for grid by adjusting inversion
    vec3 invertedColor = vec3(1.0) - terrainColor;
    float brightness = dot(invertedColor, vec3(0.299, 0.587, 0.114)); // Luminance
    if (brightness > 0.4 && brightness < 0.6) { // If color is near gray
        invertedColor = normalize(invertedColor) * 0.8; // Boost saturation
    }
    
    // Calculate distances to nearest grid boundaries
    vec2 landblockGrid = mod(worldPos, lw);
    vec2 cellGrid = mod(worldPos, cw);
    
    // Find distance to nearest boundary
    vec2 landblockDist = min(landblockGrid, lw - landblockGrid);
    vec2 cellDist = min(cellGrid, cw - cellGrid);
    
    // Create lines at boundaries using smoothstep for anti-aliasing
    float landblockLineX = uShowLandblockGrid ? 1.0 - smoothstep(0.0, scaledLandblockLineWidth, landblockDist.x) : 0.0;
    float landblockLineY = uShowLandblockGrid ? 1.0 - smoothstep(0.0, scaledLandblockLineWidth, landblockDist.y) : 0.0;
    float landblockLine = max(landblockLineX, landblockLineY);
    
    // Cell lines
    float cellLineX = uShowCellGrid ? 1.0 - smoothstep(0.0, scaledLineWidth, cellDist.x) : 0.0;
    float cellLineY = uShowCellGrid ? 1.0 - smoothstep(0.0, scaledLineWidth, cellDist.y) : 0.0;
    float cellLine = max(cellLineX, cellLineY);
    
    // Glow effect for landblock lines
    float landblockGlowX = uShowLandblockGrid ? 1.0 - smoothstep(0.0, scaledLandblockGlowWidth, landblockDist.x) : 0.0;
    float landblockGlowY = uShowLandblockGrid ? 1.0 - smoothstep(0.0, scaledLandblockGlowWidth, landblockDist.y) : 0.0;
    float landblockGlow = max(landblockGlowX, landblockGlowY);
    
    // Glow effect for cell lines
    float cellGlowX = uShowCellGrid ? 1.0 - smoothstep(0.0, scaledGlowWidth, cellDist.x) : 0.0;
    float cellGlowY = uShowCellGrid ? 1.0 - smoothstep(0.0, scaledGlowWidth, cellDist.y) : 0.0;
    float cellGlow = max(cellGlowX, cellGlowY);
    
    // Combine grid colors - landblock grid has priority
    vec3 gridColor = vec3(0.0);
    vec3 glowColor = vec3(1.0); // White glow
    
    if (showLandblockGrid && landblockLine > 0.0) {
        gridColor = uLandblockGridColor * landblockLine;
        gridColor += invertedColor * landblockGlow * (1.0 - landblockLine) * glowIntensity;
    } else if (showCellGrid && cellLine > 0.0) {
        gridColor = uCellGridColor * cellLine;
        gridColor += invertedColor * cellGlow * (1.0 - cellLine) * glowIntensity;
    } else {
        // Faint glow for areas outside main lines
        if (showLandblockGrid) {
            gridColor += uLandblockGridColor * landblockGlow * glowIntensity * 0.5;
        }
        if (showCellGrid) {
            gridColor += uCellGridColor * cellGlow * glowIntensity * 0.5;
        }
    }
    
    return gridColor * uGridOpacity;
}

void main() {
    vec4 baseColor = texture(xOverlays, vTexUV);
    vec4 combinedOverlays = vec4(0.0);
    vec4 combinedRoad = vec4(0.0);
    
    if (vOverlay0.z >= 0.0)
        combinedOverlays = combineOverlays(vTexUV, vOverlay0, vOverlay1, vOverlay2);
    if (vRoad0.z >= 0.0)
        combinedRoad = combineRoad(vTexUV, vRoad0, vRoad1);
    
    vec3 baseMasked = vec3(saturate(baseColor.rgb * ((1.0 - combinedOverlays.a) * (1.0 - combinedRoad.a))));
    vec3 overlaysMasked = vec3(saturate(combinedOverlays.rgb * (combinedOverlays.a * (1.0 - combinedRoad.a))));
    vec3 roadMasked = combinedRoad.rgb * combinedRoad.a;
    
    // Calculate base terrain color
    vec3 terrainColor = baseMasked + overlaysMasked + roadMasked;
    
    // Calculate world position for this fragment
    vec2 worldPos = vWorldPos;
    
    // Calculate grid contribution, passing terrainColor
    vec3 gridColor = calculateGrid(worldPos, terrainColor);
    
    // Blend grid with terrain
    vec3 finalColor = mix(terrainColor, gridColor, length(gridColor));
    
    FragColor = vec4(finalColor, 1.0);
    FragColor *= saturate(vLightingFactor) + xAmbient;
    FragColor.a *= uAlpha;
}