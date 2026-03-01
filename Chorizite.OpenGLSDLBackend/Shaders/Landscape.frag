#version 330 core

precision highp float;
precision highp int;
precision highp sampler2D;
precision highp sampler2DArray;

uniform sampler2DArray xOverlays;
uniform sampler2DArray xAlphas;
uniform vec3 uSunlightColor;
uniform vec3 uAmbientColor;
uniform vec3 uLightDirection;
uniform float uAlpha;

// Grid uniforms
uniform bool uShowLandblockGrid;  // Enable/disable landblock grid
uniform bool uShowCellGrid;       // Enable/disable cell grid
uniform vec3 uLandblockGridColor; // Color for landblock grid lines (RGB)
uniform vec3 uCellGridColor;      // Color for cell grid lines (RGB)
uniform float uGridLineWidth;     // Base width of grid lines in pixels
uniform float uGridOpacity;       // Opacity of grid lines (0.0 - 1.0)
uniform float uCameraDistance;    // Distance from camera to terrain
uniform float uCameraFov;         // Camera field of view in degrees
uniform float uScreenHeight;      // Screen height in pixels for scaling
uniform vec2 uGridOffset;         // Offset for grid alignment (MapOffset)

// Brush uniforms
uniform vec3 uBrushPos;       // World position of the brush center
uniform float uBrushRadius;   // Radius of the brush in world units
uniform vec4 uBrushColor;     // Color of the brush overlay (RGBA)
uniform bool uShowBrush;      // Toggle brush visibility
uniform int uBrushShape;      // 0 = Circle, 1 = Square, 2 = Crosshair

uniform bool uShowUnwalkableSlopes;
uniform float uFloorZ;

in vec3 vTexUV;
in vec4 vOverlay0;
in vec4 vOverlay1;
in vec4 vOverlay2;
in vec4 vRoad0;
in vec4 vRoad1;
in float vLightingFactor;
in vec2 vWorldPos;
in vec3 vNormal;

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
        overlay0 = texture(xOverlays, vec3(uvb, round(pOverlay0.z)));
        // Only sample alpha if alphaIdx is valid
        if (pOverlay0.w >= 0.0) {
            overlayAlpha0 = texture(xAlphas, vec3(pOverlay0.xy, round(pOverlay0.w)));
            overlay0.a = overlayAlpha0.a;
        }
    }
    if (h1 > 0.0) {
        overlay1 = texture(xOverlays, vec3(uvb, round(pOverlay1.z)));
        if (pOverlay1.w >= 0.0) {
            overlayAlpha1 = texture(xAlphas, vec3(pOverlay1.xy, round(pOverlay1.w)));
            overlay1.a = overlayAlpha1.a;
        }
    }
    if (h2 > 0.0) {
        overlay2 = texture(xOverlays, vec3(uvb, round(pOverlay2.z)));
        if (pOverlay2.w >= 0.0) {
            overlayAlpha2 = texture(xAlphas, vec3(pOverlay2.xy, round(pOverlay2.w)));
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
        result = texture(xOverlays, vec3(uvb, round(pRoad0.z)));
        if (pRoad0.w >= 0.0) {
            vec4 roadAlpha0 = texture(xAlphas, vec3(pRoad0.xy, round(pRoad0.w)));
            result.a = 1.0 - roadAlpha0.a;
            if (h1 > 0.0 && pRoad1.w >= 0.0) {
                vec4 roadAlpha1 = texture(xAlphas, vec3(pRoad1.xy, round(pRoad1.w)));
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

vec4 calculateGrid(vec2 worldPos, vec3 terrainColor) {
    // Early out if both grids are disabled
    if (!uShowLandblockGrid && !uShowCellGrid) {
        return vec4(0.0);
    }
    
    float lw = 192.0; // Landblock width
    float cw = 24.0;  // Cell width
    float glowWidthFactor = 1.5; // Glow extends wider than the line
    float glowIntensity = 0.5;   // Adjusted glow intensity
    float landblockLineWidthFactor = 2.0; // Double the thickness for landblock lines

    // Calculate pixel size in world units
    float fovRad = uCameraFov * 0.0174532925; // degrees to radians
    float worldUnitsPerPixel = uCameraDistance * tan(fovRad / 2.0) * 2.0 / uScreenHeight;
    float scaledLineWidth = uGridLineWidth * worldUnitsPerPixel;
    // float scaledGlowWidth = scaledLineWidth * glowWidthFactor;
    // float scaledLandblockGlowWidth = scaledGlowWidth * landblockLineWidthFactor; // Thicker glow for landblock lines

    // Determine if cell grid is visible
    bool showCellGrid = (cw / 2.0 > worldUnitsPerPixel);
    bool showLandblockGrid = (lw / 2.0 > worldUnitsPerPixel);

    // Use normal line width for landblock lines if cell grid is not visible
    float scaledLandblockLineWidth = showCellGrid ? scaledLineWidth * landblockLineWidthFactor : scaledLineWidth;

    if (!showLandblockGrid && !showCellGrid) {
        return vec4(0.0);
    }
    
    // Calculate distances to nearest grid boundaries using round() for better stability at boundaries
    // The previous mod() collection had issues at 0 and multiples of the size due to precision.
    vec2 nearbyLandblockLine = round((worldPos - uGridOffset) / lw) * lw + uGridOffset;
    vec2 landblockDist = abs(worldPos - nearbyLandblockLine);
    
    vec2 nearbyCellLine = round((worldPos - uGridOffset) / cw) * cw + uGridOffset;
    vec2 cellDist = abs(worldPos - nearbyCellLine);
    
    // Create lines at boundaries using smoothstep for anti-aliasing
    // We only care about the closest line in either X or Y
    float landblockLineX = uShowLandblockGrid ? 1.0 - smoothstep(0.0, scaledLandblockLineWidth, landblockDist.x) : 0.0;
    float landblockLineY = uShowLandblockGrid ? 1.0 - smoothstep(0.0, scaledLandblockLineWidth, landblockDist.y) : 0.0;
    float landblockLine = max(landblockLineX, landblockLineY);
    
    // Cell lines
    float cellLineX = uShowCellGrid ? 1.0 - smoothstep(0.0, scaledLineWidth, cellDist.x) : 0.0;
    float cellLineY = uShowCellGrid ? 1.0 - smoothstep(0.0, scaledLineWidth, cellDist.y) : 0.0;
    float cellLine = max(cellLineX, cellLineY);
    
    
    // Combine grid colors - landblock grid has priority
    vec3 gridColor = vec3(0.0);
    float gridAlpha = 0.0;
    
    if (showLandblockGrid && landblockLine > 0.0) {
        gridColor = uLandblockGridColor;
        gridAlpha = landblockLine;
    } else if (showCellGrid && cellLine > 0.0) {
        gridColor = uCellGridColor;
        gridAlpha = cellLine;
    }
    
    return vec4(gridColor, gridAlpha * uGridOpacity);
}

// SDF Functions
float sdCircle(vec2 p, float r) {
    return length(p) - r;
}

float sdRoundedBox(in vec2 p, in vec2 b, in float r) {
    vec2 q = abs(p) - b + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
}

float sdCross(in vec2 p, in float r, in float w) {
    vec2 d = abs(p);
    return min(max(d.x - w, d.y - r), max(d.y - w, d.x - r));
}

vec4 calculateBrush(vec2 worldPos) { 
    if (!uShowBrush) return vec4(0.0);

    vec2 p = worldPos - uBrushPos.xy;
    float dist = 0.0;
    
    if (uBrushShape == 1) {
        // Rounded Box (Square)
        float cornerRadius = min(uBrushRadius * 0.25, 10.0);
        dist = sdRoundedBox(p, vec2(uBrushRadius), cornerRadius);
    } else if (uBrushShape == 2) {
        // Crosshair
        float crossWidth = 1.0;
        float crossSize = 12.0;
        dist = sdCross(p, crossSize, crossWidth);
    } else {
        // Circle
        dist = sdCircle(p, uBrushRadius);
    }
    
    // Calculate outline
    float fovRad = uCameraFov * 0.0174532925; // degrees to radians
    float pixelSize = (uCameraDistance * tan(fovRad / 2.0) * 2.0 / uScreenHeight);
    float lineWidth = 2.0 * pixelSize;
    float feather = 1.0 * pixelSize;
    float outline = 1.0 - smoothstep(lineWidth - feather, lineWidth, abs(dist));
    
    float fill = (uBrushShape == 2) ? 0.0 : (1.0 - smoothstep(0.0, feather, dist)) * 0.1;
    float alpha = max(outline, fill);
    
    if (alpha <= 0.0) return vec4(0.0);
    
    vec4 brushColor = uBrushColor;
    if (brushColor.a == 0.0) brushColor = vec4(0.0, 1.0, 0.0, 1.0);
    
    return vec4(brushColor.rgb, alpha * brushColor.a);
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
    vec4 gridColor = calculateGrid(worldPos, terrainColor);
    
    // Blend grid with terrain
    vec3 finalColor = mix(terrainColor, gridColor.rgb, gridColor.a);
    
    // Apply Brush Overlay
    vec4 brushColor = calculateBrush(worldPos);
    finalColor = mix(finalColor, brushColor.rgb, brushColor.a);
    
    // Highlight unwalkable slopes
    if (uShowUnwalkableSlopes && normalize(vNormal).z < uFloorZ) {
        finalColor = mix(finalColor, vec3(1.0, 0.0, 0.0), 0.5);
    }
    
    // Lighting
    float diff = max(dot(normalize(vNormal), normalize(uLightDirection)), 0.0);
    vec3 lighting = clamp(uAmbientColor + uSunlightColor * diff, 0.0, 1.0);
    vec3 litColor = finalColor * lighting;
    FragColor = vec4(litColor, uAlpha);
}