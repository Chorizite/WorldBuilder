using System.Numerics;
using Raylib_cs;
using System;
using System.Diagnostics;
using Color = Raylib_cs.Color;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Fun {
    public unsafe class ModelGroupRenderer : IDisposable {
        private FunViewModel _viewModel;
        private Model logoModel;
        private Model asheronsTextModel;
        private Model callTextModel;

        // Gold material textures
        private Texture2D goldAlbedoTexture;
        private Texture2D goldMetalnessTexture;
        private Texture2D goldNormalTexture;
        private Texture2D goldRoughnessTexture;
        //private Texture2D goldEmissiveTexture;

        // Silver material textures
        private Texture2D silverAlbedoTexture;
        private Texture2D silverMetalnessTexture;
        private Texture2D silverNormalTexture;
        private Texture2D silverRoughnessTexture;
        //private Texture2D silverEmissiveTexture;

        private Material goldMaterial;
        private Material silverMaterial;
        private Shader pbrShader;
        private float rotationAngle;
        private float lightArcTime;
        internal float lightDepth = 5.4f;
        internal Vector3 lightPosition;

        // Spin control variables
        private float spinSpeed = 45.0f;
        private bool isDragging = false;
        private Vector2 lastMousePosition;
        private float baseSpin = 45.0f;
        private const float minSpinSpeed = -1540.0f;
        private const float maxSpinSpeed = 1540.0f;
        private const float dragSensitivity = 1.0f;

        // Light management
        private const int MAX_LIGHTS = 4;
        private struct Light {
            public int Enabled;
            public int Type; // 0 = Directional, 1 = Point, 2 = Spot
            public Vector3 Position;
            public Vector3 Target;
            public Color Color;
            public float Intensity;
            public int EnabledLoc;
            public int TypeLoc;
            public int PositionLoc;
            public int TargetLoc;
            public int ColorLoc;
            public int IntensityLoc;
        }
        private Light[] lights;
        private int lightCount;

        public ModelGroupRenderer(FunViewModel vieWModel) {
            _viewModel = vieWModel;
            // Load models
            logoModel = Raylib.LoadModel("Resources/Models/logo.obj");
            asheronsTextModel = Raylib.LoadModel("Resources/Models/asheronstext.obj");
            callTextModel = Raylib.LoadModel("Resources/Models/calltext.obj");
            ReverseModelNormals(ref logoModel, "Logo");

            // Load textures
            goldAlbedoTexture = Raylib.LoadTexture("Resources/Textures/gold-albedo.png");
            goldMetalnessTexture = Raylib.LoadTexture("Resources/Textures/gold-metallic.png");
            goldNormalTexture = Raylib.LoadTexture("Resources/Textures/gold-normal.png");
            goldRoughnessTexture = Raylib.LoadTexture("Resources/Textures/gold-roughness.png");
            //goldEmissiveTexture = Raylib.LoadTexture("Resources/Textures/gold-emissive.png");

            silverAlbedoTexture = Raylib.LoadTexture("Resources/Textures/silver-albedo.png");
            silverMetalnessTexture = Raylib.LoadTexture("Resources/Textures/silver-metallic.png");
            silverNormalTexture = Raylib.LoadTexture("Resources/Textures/silver-normal.png");
            silverRoughnessTexture = Raylib.LoadTexture("Resources/Textures/silver-roughness.png");
            //silverEmissiveTexture = Raylib.LoadTexture("Resources/Textures/silver-emissive.png");

            // Load custom PBR shader
            pbrShader = Raylib.LoadShader("Resources/Shaders/pbr.vs", "Resources/Shaders/pbr.fs");

            // Setup shader locations
            pbrShader.Locs[(int)ShaderLocationIndex.MapAlbedo] = Raylib.GetShaderLocation(pbrShader, "albedoMap");
            pbrShader.Locs[(int)ShaderLocationIndex.MapMetalness] = Raylib.GetShaderLocation(pbrShader, "mraMap");
            pbrShader.Locs[(int)ShaderLocationIndex.MapNormal] = Raylib.GetShaderLocation(pbrShader, "normalMap");
            //pbrShader.Locs[(int)ShaderLocationIndex.MapEmission] = Raylib.GetShaderLocation(pbrShader, "emissiveMap");
            pbrShader.Locs[(int)ShaderLocationIndex.ColorDiffuse] = Raylib.GetShaderLocation(pbrShader, "albedoColor");

            // Initialize lights (single mouse-controlled light)
            lights = new Light[MAX_LIGHTS];
            lightCount = 0;
            CreateLight(1, new Vector3(0.0f, 1.0f, 0.0f), Vector3.Zero, Color.White, 4.0f); // Initial light
            // Disable other lights
            for (int i = 1; i < MAX_LIGHTS; i++) {
                CreateLight(1, Vector3.Zero, Vector3.Zero, Color.Black, 0.0f, false);
            }

            // Set ambient properties
            float ambientIntensity = 0.02f;
            Color ambientColor = new Color(255, 255, 255, 255);
            Vector3 ambientColorNormalized = new Vector3(ambientColor.R / 255.0f, ambientColor.G / 255.0f, ambientColor.B / 255.0f);
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "ambientColor"), new float[] { ambientColorNormalized.X, ambientColorNormalized.Y, ambientColorNormalized.Z }, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "ambient"), new float[] { ambientIntensity }, ShaderUniformDataType.Float);

            // Set max light count
            int maxLightCount = MAX_LIGHTS;
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "numOfLights"), new int[] { maxLightCount }, ShaderUniformDataType.Int);

            // Enable texture usage
            int usage = 0;
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "useTexAlbedo"), new int[] { usage }, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "useTexNormal"), new int[] { usage }, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "useTexMRA"), new int[] { usage }, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "useTexEmissive"), new int[] { usage }, ShaderUniformDataType.Int);

            // Create PBR materials
            CreatePBRMaterials();

            // Apply materials to models
            ApplyMaterialToModel(ref logoModel, "Logo", goldMaterial);
            ApplyMaterialToModel(ref asheronsTextModel, "AsheronsText", silverMaterial);
            ApplyMaterialToModel(ref callTextModel, "CallText", silverMaterial);

            rotationAngle = 0.0f;
            lightArcTime = 0.0f;
        }

        private void CreateLight(int type, Vector3 position, Vector3 target, Color color, float intensity, bool enabled = true) {
            if (lightCount >= MAX_LIGHTS) return;

            var light = new Light {
                Enabled = enabled ? 1 : 0,
                Type = type,
                Position = position,
                Target = target,
                Color = color,
                Intensity = intensity,
                EnabledLoc = Raylib.GetShaderLocation(pbrShader, $"lights[{lightCount}].enabled"),
                TypeLoc = Raylib.GetShaderLocation(pbrShader, $"lights[{lightCount}].type"),
                PositionLoc = Raylib.GetShaderLocation(pbrShader, $"lights[{lightCount}].position"),
                TargetLoc = Raylib.GetShaderLocation(pbrShader, $"lights[{lightCount}].target"),
                ColorLoc = Raylib.GetShaderLocation(pbrShader, $"lights[{lightCount}].color"),
                IntensityLoc = Raylib.GetShaderLocation(pbrShader, $"lights[{lightCount}].intensity")
            };

            UpdateLight(light);
            lights[lightCount] = light;
            lightCount++;
        }

        private void UpdateLight(Light light) {
            Raylib.SetShaderValue(pbrShader, light.EnabledLoc, new int[] { light.Enabled }, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(pbrShader, light.TypeLoc, new int[] { light.Type }, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(pbrShader, light.PositionLoc, new float[] { light.Position.X, light.Position.Y, light.Position.Z }, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(pbrShader, light.TargetLoc, new float[] { light.Target.X, light.Target.Y, light.Target.Z }, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(pbrShader, light.ColorLoc, new float[] { light.Color.R / 255.0f, light.Color.G / 255.0f, light.Color.B / 255.0f, light.Color.A / 255.0f }, ShaderUniformDataType.Vec4);
            Raylib.SetShaderValue(pbrShader, light.IntensityLoc, new float[] { light.Intensity }, ShaderUniformDataType.Float);
        }

        private void ReverseModelNormals(ref Model model, string modelName) {
            unsafe {
                for (int i = 0; i < model.MeshCount; i++) {
                    Mesh mesh = model.Meshes[i];
                    if (mesh.Normals == null) {
                        Console.WriteLine($"[WARNING] {modelName}: Mesh {i} has no normals to reverse.");
                        continue;
                    }
                    float* normals = mesh.Normals;
                    int vertexCount = mesh.VertexCount;
                    for (int j = 0; j < vertexCount * 3; j += 3) {
                        normals[j] = -normals[j];
                        normals[j + 1] = -normals[j + 1];
                        normals[j + 2] = -normals[j + 2];
                    }
                    Raylib.UpdateMeshBuffer(mesh, 2, normals, vertexCount * 3 * sizeof(float), 0);
                    Console.WriteLine($"[DEBUG] {modelName}: Reversed normals for mesh {i}.");
                }
            }
        }

        private void CreatePBRMaterials() {
            // Create gold material
            goldMaterial = Raylib.LoadMaterialDefault();
            goldMaterial.Shader = pbrShader;

            unsafe {
                goldMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = goldAlbedoTexture;
                goldMaterial.Maps[(int)MaterialMapIndex.Metalness].Texture = goldMetalnessTexture;
                goldMaterial.Maps[(int)MaterialMapIndex.Normal].Texture = goldNormalTexture;
                //goldMaterial.Maps[(int)MaterialMapIndex.Emission].Texture = goldEmissiveTexture;

                goldMaterial.Maps[(int)MaterialMapIndex.Albedo].Color = Color.White;
                goldMaterial.Maps[(int)MaterialMapIndex.Metalness].Value = 0.9f;
                goldMaterial.Maps[(int)MaterialMapIndex.Roughness].Value = 0.2f;
                goldMaterial.Maps[(int)MaterialMapIndex.Occlusion].Value = 1.0f;
                goldMaterial.Maps[(int)MaterialMapIndex.Emission].Color = Color.Gold;

                SetTextureWrapRepeat(goldMaterial);

                Console.WriteLine($"[DEBUG] Gold material created with shader ID: {goldMaterial.Shader.Id}");
            }

            // Create silver material
            silverMaterial = Raylib.LoadMaterialDefault();
            silverMaterial.Shader = pbrShader;

            unsafe {
                silverMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = silverAlbedoTexture;
                silverMaterial.Maps[(int)MaterialMapIndex.Metalness].Texture = silverMetalnessTexture;
                silverMaterial.Maps[(int)MaterialMapIndex.Normal].Texture = silverNormalTexture;
                //silverMaterial.Maps[(int)MaterialMapIndex.Emission].Texture = silverEmissiveTexture;

                silverMaterial.Maps[(int)MaterialMapIndex.Albedo].Color = Color.White;
                silverMaterial.Maps[(int)MaterialMapIndex.Metalness].Value = 0.8f;
                silverMaterial.Maps[(int)MaterialMapIndex.Roughness].Value = 0.8f;
                silverMaterial.Maps[(int)MaterialMapIndex.Occlusion].Value = 1.0f;
                silverMaterial.Maps[(int)MaterialMapIndex.Emission].Color = Color.White;

                SetTextureWrapRepeat(silverMaterial);

                Console.WriteLine($"[DEBUG] Silver material created with shader ID: {silverMaterial.Shader.Id}");
            }
        }

        private void SetTextureWrapRepeat(Material material) {
            unsafe {
                Raylib.SetTextureWrap(material.Maps[(int)MaterialMapIndex.Albedo].Texture, TextureWrap.Repeat);
                Raylib.SetTextureWrap(material.Maps[(int)MaterialMapIndex.Metalness].Texture, TextureWrap.Repeat);
                Raylib.SetTextureWrap(material.Maps[(int)MaterialMapIndex.Normal].Texture, TextureWrap.Repeat);
                Raylib.SetTextureWrap(material.Maps[(int)MaterialMapIndex.Emission].Texture, TextureWrap.Repeat);
            }
        }

        private void ApplyMaterialToModel(ref Model model, string modelName, Material mat) {
            unsafe {
                for (int i = 0; i < model.MaterialCount; i++) {
                    model.Materials[i] = mat;
                    Console.WriteLine($"[DEBUG] {modelName}: Applied material with shader ID {mat.Shader.Id}");
                }
            }
        }

        private void HandleSpinControl() {
            Vector2 currentMousePosition = new Vector2(Raylib.GetMouseX(), Raylib.GetMouseY());

            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                isDragging = true;
                lastMousePosition = currentMousePosition;
            }
            else if (Raylib.IsMouseButtonReleased(MouseButton.Left)) {
                isDragging = false;
            }

            if (isDragging && Raylib.IsMouseButtonDown(MouseButton.Left)) {
                Vector2 mouseDelta = new Vector2(
                    currentMousePosition.X - lastMousePosition.X,
                    currentMousePosition.Y - lastMousePosition.Y
                );

                float deltaX = mouseDelta.X;
                spinSpeed += deltaX * dragSensitivity;
                spinSpeed = Math.Clamp(spinSpeed, minSpinSpeed, maxSpinSpeed);
                lastMousePosition = currentMousePosition;
            }
            else if (Math.Abs(spinSpeed) > baseSpin) {
                float frictionRate = 2.0f;
                float frictionFactor = (float)Math.Pow(0.5, frictionRate * Raylib.GetFrameTime());
                spinSpeed *= frictionFactor;
            }
        }

        public unsafe void Update(float deltaTime, Camera3D camera) {
            var cameraPosition = camera.Position;

            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "ambient"), new float[] { _viewModel.AmbientIntensity }, ShaderUniformDataType.Float);

            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "useTexAlbedo"), new int[] { _viewModel.UseTexAlbedo ? 1 : 0 }, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "useTexNormal"), new int[] { _viewModel.UseTexNormal ? 1 : 0 }, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "useTexMRA"), new int[] { _viewModel.UseTexMRA ? 1 : 0 }, ShaderUniformDataType.Int);
            Raylib.SetShaderValue(pbrShader, Raylib.GetShaderLocation(pbrShader, "useTexEmissive"), new int[] { 0 }, ShaderUniformDataType.Int);

            // Update material properties from view model
            goldMaterial.Maps[(int)MaterialMapIndex.Metalness].Value = _viewModel.GoldMetallness;
            goldMaterial.Maps[(int)MaterialMapIndex.Roughness].Value = _viewModel.GoldRoughness;
            goldMaterial.Maps[(int)MaterialMapIndex.Occlusion].Value = _viewModel.GoldOcclusion;

            silverMaterial.Maps[(int)MaterialMapIndex.Metalness].Value = _viewModel.SilverMetallness;
            silverMaterial.Maps[(int)MaterialMapIndex.Roughness].Value = _viewModel.SilverRoughness;
            silverMaterial.Maps[(int)MaterialMapIndex.Occlusion].Value = _viewModel.SilverOcclusion;

            // Update shader material properties
            int metallicValueLoc = Raylib.GetShaderLocation(pbrShader, "metallicValue");
            int roughnessValueLoc = Raylib.GetShaderLocation(pbrShader, "roughnessValue");
            int emissiveIntensityLoc = Raylib.GetShaderLocation(pbrShader, "emissivePower");
            int emissiveColorLoc = Raylib.GetShaderLocation(pbrShader, "emissiveColor");

            // Update light position based on mouse ray
            lightDepth += Raylib.GetMouseWheelMove() * 0.3f;
            Vector2 mousePosition = new Vector2(Raylib.GetMouseX(), Raylib.GetMouseY());
            Ray mouseRay = Raylib.GetMouseRay(mousePosition, camera);
            lightPosition = cameraPosition + mouseRay.Direction * lightDepth;

            // Update single light (lights[0])
            var light = lights[0];
            light.Position = lightPosition;
            light.Intensity = _viewModel.LightIntensity;
            light.Type = 0;
            UpdateLight(light);
            lights[0] = light;

            // Update other lights to ensure they remain disabled
            for (int i = 1; i < lightCount; i++) {
                var disabledLight = lights[i];
                disabledLight.Enabled = 0;
                UpdateLight(disabledLight);
                lights[i] = disabledLight;
            }

            // Handle spin control input
            HandleSpinControl();

            // Update rotation
            rotationAngle += spinSpeed * deltaTime;
            if (rotationAngle >= 360.0f) rotationAngle -= 360.0f;
            else if (rotationAngle < 0.0f) rotationAngle += 360.0f;

            // Update camera position in shader
            int viewPosLoc = Raylib.GetShaderLocation(pbrShader, "viewPos");
            Raylib.SetShaderValue(pbrShader, viewPosLoc, new float[] { cameraPosition.X, cameraPosition.Y, cameraPosition.Z }, ShaderUniformDataType.Vec3);

            // Update model and normal matrices
            Matrix4x4 rotationMatrix = Matrix4x4.CreateRotationY(rotationAngle * Raylib.DEG2RAD);
            int modelMatrixLoc = Raylib.GetShaderLocation(pbrShader, "matModel");
            Raylib.SetShaderValueMatrix(pbrShader, modelMatrixLoc, rotationMatrix);

            Matrix4x4 normalMatrix = Matrix4x4.Transpose(rotationMatrix);
            Matrix4x4.Invert(normalMatrix, out var inverted);
            int normalMatrixLoc = Raylib.GetShaderLocation(pbrShader, "matNormal");
            Raylib.SetShaderValueMatrix(pbrShader, normalMatrixLoc, inverted);

            // Update texture tiling
            int textureTilingLoc = Raylib.GetShaderLocation(pbrShader, "tiling");
            float defaultScale = 0.4f;
            Raylib.SetShaderValue(pbrShader, textureTilingLoc, new float[] { defaultScale, defaultScale }, ShaderUniformDataType.Vec2);
        }

        public void Render(Camera3D camera) {
            Rlgl.DisableBackfaceCulling();
            float screenWidth = Raylib.GetScreenWidth();
            float screenHeight = Raylib.GetScreenHeight();
            float aspectRatio = screenWidth / screenHeight;

            BoundingBox logoBounds = Raylib.GetModelBoundingBox(logoModel);
            BoundingBox asheronsBounds = Raylib.GetModelBoundingBox(asheronsTextModel);
            BoundingBox callBounds = Raylib.GetModelBoundingBox(callTextModel);

            Vector3 min = new Vector3(
                Math.Min(logoBounds.Min.X, Math.Min(asheronsBounds.Min.X, callBounds.Min.X)),
                Math.Min(logoBounds.Min.Y, Math.Min(asheronsBounds.Min.Y, callBounds.Min.Y)),
                Math.Min(logoBounds.Min.Z, Math.Min(asheronsBounds.Min.Z, callBounds.Min.Z))
            );
            Vector3 max = new Vector3(
                Math.Max(logoBounds.Max.X, Math.Max(asheronsBounds.Max.X, callBounds.Max.X)),
                Math.Max(logoBounds.Max.Y, Math.Max(asheronsBounds.Max.Y, callBounds.Max.Y)),
                Math.Max(logoBounds.Max.Z, Math.Max(asheronsBounds.Max.Z, callBounds.Max.Z))
            );
            var bounds = new BoundingBox(min, max);

            Vector3 size = new Vector3(
                bounds.Max.X - bounds.Min.X,
                bounds.Max.Y - bounds.Min.Y,
                bounds.Max.Z - bounds.Min.Z
            );

            float fovY = camera.FovY * Raylib.DEG2RAD;
            float fovX = 2.0f * MathF.Atan(MathF.Tan(fovY / 2.0f) * aspectRatio);
            float maxDimension = Math.Max(size.X, Math.Max(size.Y, size.Z));
            float distance = 5.0f;
            float scale = Math.Min(
                (distance * MathF.Tan(fovX / 2.0f)) / (size.X / 2.0f),
                (distance * MathF.Tan(fovY / 2.0f)) / (size.Y / 2.0f)
            ) * 0.9f;
            
            int textureTilingLoc = Raylib.GetShaderLocation(pbrShader, "tiling");
            int emissiveColorLoc = Raylib.GetShaderLocation(pbrShader, "emissiveColor");
            int emissiveIntensityLoc = Raylib.GetShaderLocation(pbrShader, "emissivePower");
            int metallicValueLoc = Raylib.GetShaderLocation(pbrShader, "metallicValue");
            int roughnessValueLoc = Raylib.GetShaderLocation(pbrShader, "roughnessValue");
            int aoValueLoc = Raylib.GetShaderLocation(pbrShader, "aoValue");

            // Render logo model (gold material)
            float goldScale = 1.0f;
            Raylib.SetShaderValue(pbrShader, textureTilingLoc, new float[] { goldScale, goldScale }, ShaderUniformDataType.Vec2);

            Vector4 goldEmissiveColor = new Vector4(
                goldMaterial.Maps[(int)MaterialMapIndex.Emission].Color.R / 255.0f,
                goldMaterial.Maps[(int)MaterialMapIndex.Emission].Color.G / 255.0f,
                goldMaterial.Maps[(int)MaterialMapIndex.Emission].Color.B / 255.0f,
                goldMaterial.Maps[(int)MaterialMapIndex.Emission].Color.A / 255.0f
            );

            Raylib.SetShaderValue(pbrShader, emissiveColorLoc, new float[] { goldEmissiveColor.X, goldEmissiveColor.Y, goldEmissiveColor.Z, goldEmissiveColor.W }, ShaderUniformDataType.Vec4);
            float emissiveIntensity = 0.01f;
            Raylib.SetShaderValue(pbrShader, emissiveIntensityLoc, new float[] { emissiveIntensity }, ShaderUniformDataType.Float);

            // *** THESE ARE THE IMPORTANT FIXES ***
            Raylib.SetShaderValue(pbrShader, metallicValueLoc, new float[] { goldMaterial.Maps[(int)MaterialMapIndex.Metalness].Value }, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(pbrShader, roughnessValueLoc, new float[] { goldMaterial.Maps[(int)MaterialMapIndex.Roughness].Value }, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(pbrShader, aoValueLoc, new float[] { goldMaterial.Maps[(int)MaterialMapIndex.Occlusion].Value }, ShaderUniformDataType.Float);

            Raylib.DrawModelEx(logoModel, new Vector3(0.0f, 0f, -3f),
                              new Vector3(0.0f, 1.0f, 0.0f), rotationAngle,
                              new Vector3(scale, scale, scale) * 2.0f, Color.Gold);

            // Render text models (silver material)
            float silverScale = 1.0f;
            Raylib.SetShaderValue(pbrShader, textureTilingLoc, new float[] { silverScale, silverScale }, ShaderUniformDataType.Vec2);

            Vector4 silverEmissiveColor = new Vector4(
                silverMaterial.Maps[(int)MaterialMapIndex.Emission].Color.R / 255.0f,
                silverMaterial.Maps[(int)MaterialMapIndex.Emission].Color.G / 255.0f,
                silverMaterial.Maps[(int)MaterialMapIndex.Emission].Color.B / 255.0f,
                silverMaterial.Maps[(int)MaterialMapIndex.Emission].Color.A / 255.0f
            );

            Raylib.SetShaderValue(pbrShader, emissiveColorLoc, new float[] { silverEmissiveColor.X, silverEmissiveColor.Y, silverEmissiveColor.Z, silverEmissiveColor.W }, ShaderUniformDataType.Vec4);
            Raylib.SetShaderValue(pbrShader, emissiveIntensityLoc, new float[] { emissiveIntensity }, ShaderUniformDataType.Float);

            // *** SET SILVER MATERIAL PROPERTIES ***
            Raylib.SetShaderValue(pbrShader, metallicValueLoc, new float[] { silverMaterial.Maps[(int)MaterialMapIndex.Metalness].Value }, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(pbrShader, roughnessValueLoc, new float[] { silverMaterial.Maps[(int)MaterialMapIndex.Roughness].Value }, ShaderUniformDataType.Float);
            Raylib.SetShaderValue(pbrShader, aoValueLoc, new float[] { silverMaterial.Maps[(int)MaterialMapIndex.Occlusion].Value }, ShaderUniformDataType.Float);

            Raylib.DrawModel(asheronsTextModel, new Vector3(0.0f, 0, 1.01f), scale, Color.White);
            Raylib.DrawModel(callTextModel, new Vector3(0.15f, 0, 1.0f), scale, Color.White);
        }

        public void Dispose() {
            Raylib.UnloadModel(logoModel);
            Raylib.UnloadModel(asheronsTextModel);
            Raylib.UnloadModel(callTextModel);

            Raylib.UnloadShader(pbrShader);

            Raylib.UnloadMaterial(goldMaterial);
            Raylib.UnloadMaterial(silverMaterial);

            Raylib.UnloadTexture(goldAlbedoTexture);
            Raylib.UnloadTexture(goldMetalnessTexture);
            Raylib.UnloadTexture(goldNormalTexture);
            Raylib.UnloadTexture(goldRoughnessTexture);
            //Raylib.UnloadTexture(goldEmissiveTexture);

            Raylib.UnloadTexture(silverAlbedoTexture);
            Raylib.UnloadTexture(silverMetalnessTexture);
            Raylib.UnloadTexture(silverNormalTexture);
            Raylib.UnloadTexture(silverRoughnessTexture);
            //Raylib.UnloadTexture(silverEmissiveTexture);
        }
    }
}