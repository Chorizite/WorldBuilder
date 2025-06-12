using System.Numerics;
using Raylib_cs;
using System;
using System.Diagnostics;
using Color = Raylib_cs.Color;

namespace WorldBuilder.Fun {
    public class ModelGroupRenderer : IDisposable {
        private Model logoModel;
        private Model asheronsTextModel;
        private Model callTextModel;

        // Gold material textures (keeping original)
        private Texture2D goldAlbedoTexture;
        private Texture2D goldMetalnessTexture;
        private Texture2D goldNormalTexture;
        private Texture2D goldRoughnessTexture;
        private Texture2D goldDisplacementTexture;
        private Texture2D goldOcclusionTexture;

        // Silver material textures (new corroded metal)
        private Texture2D silverAlbedoTexture;
        private Texture2D silverMetalnessTexture;
        private Texture2D silverNormalTexture;
        private Texture2D silverRoughnessTexture;
        private Texture2D silverDisplacementTexture;
        private Material goldMaterial;
        private Material silverMaterial;
        private Shader pbrShader;
        private float rotationAngle;
        private float lightArcTime;
        internal float lightDepth = 5.4f;
        internal Vector3 lightPosition;

        // Spin control variables
        private float spinSpeed = 45.0f; // Default spin speed (degrees per second)
        private bool isDragging = false;
        private Vector2 lastMousePosition;
        private float baseSpin = 45.0f; // Base spin speed to return to
        private const float minSpinSpeed = -1540.0f; // Allow reverse spinning
        private const float maxSpinSpeed = 1540.0f;
        private const float dragSensitivity = 1.0f; // How sensitive the drag is

        public ModelGroupRenderer() {
            // Load models
            logoModel = Raylib.LoadModel("Resources/Models/logo.obj");
            asheronsTextModel = Raylib.LoadModel("Resources/Models/asheronstext.obj");
            callTextModel = Raylib.LoadModel("Resources/Models/calltext.obj");
            ReverseModelNormals(ref logoModel, "Logo");

            goldAlbedoTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_COL_1K_METALNESS.jpg");
            goldMetalnessTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_METALNESS_1K_METALNESS.jpg");
            goldNormalTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_NRM_1K_METALNESS.jpg");
            goldRoughnessTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_ROUGHNESS_1K_METALNESS.jpg");
            goldDisplacementTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_DISP_1K_METALNESS.jpg");

            silverAlbedoTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_COL_1K_METALNESS.jpg");
            silverMetalnessTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_METALNESS_1K_METALNESS.jpg");
            silverNormalTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_NRM_1K_METALNESS.jpg");
            silverRoughnessTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_ROUGHNESS_1K_METALNESS.jpg");
            silverDisplacementTexture = Raylib.LoadTexture("Resources/Textures/MetalCorrodedHeavy001_DISP_1K_METALNESS.jpg");

            // Load custom PBR shader for texture scaling
            pbrShader = Raylib.LoadShader("Resources/Shaders/pbr.vs", "Resources/Shaders/pbr.fs");

            // Create PBR materials
            CreatePBRMaterials();

            // Apply materials to models
            ApplyMaterialToModel(ref logoModel, "Logo", goldMaterial);
            ApplyMaterialToModel(ref asheronsTextModel, "AsheronsText", silverMaterial);
            ApplyMaterialToModel(ref callTextModel, "CallText", silverMaterial);

            rotationAngle = 0.0f;
            lightArcTime = 0.0f;
        }

        private void ReverseModelNormals(ref Model model, string modelName) {
            unsafe {
                for (int i = 0; i < model.MeshCount; i++) {
                    Mesh mesh = model.Meshes[i];

                    // Ensure the mesh has normals
                    if (mesh.Normals == null) {
                        Console.WriteLine($"[WARNING] {modelName}: Mesh {i} has no normals to reverse.");
                        continue;
                    }

                    // Access the normals array
                    float* normals = mesh.Normals;
                    int vertexCount = mesh.VertexCount;

                    // Invert each normal (x, y, z)
                    for (int j = 0; j < vertexCount * 3; j += 3) {
                        normals[j] = -normals[j];     // Invert X
                        normals[j + 1] = -normals[j + 1]; // Invert Y
                        normals[j + 2] = -normals[j + 2]; // Invert Z
                    }

                    // Update the mesh on the GPU
                    Raylib.UpdateMeshBuffer(mesh, 2, normals, vertexCount * 3 * sizeof(float), 0);
                    Console.WriteLine($"[DEBUG] {modelName}: Reversed normals for mesh {i}.");
                }
            }
        }

        private void CreatePBRMaterials() {
            // Create gold material
            goldMaterial = Raylib.LoadMaterialDefault();
            goldMaterial.Shader = pbrShader; // Use custom shader

            unsafe {
                goldMaterial.Maps[(int)MaterialMapIndex.Metalness].Value = 0.9f;
                goldMaterial.Maps[(int)MaterialMapIndex.Roughness].Value = 0.2f;
                goldMaterial.Maps[(int)MaterialMapIndex.Occlusion].Value = 1.0f;

                goldMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = goldAlbedoTexture;
                goldMaterial.Maps[(int)MaterialMapIndex.Metalness].Texture = goldMetalnessTexture;
                goldMaterial.Maps[(int)MaterialMapIndex.Normal].Texture = goldNormalTexture;
                goldMaterial.Maps[(int)MaterialMapIndex.Roughness].Texture = goldRoughnessTexture;

                SetTextureWrapRepeat(goldMaterial);

                Console.WriteLine($"[DEBUG] Gold material created with shader ID: {goldMaterial.Shader.Id}");
            }

            // Create silver material
            silverMaterial = Raylib.LoadMaterialDefault();
            silverMaterial.Shader = pbrShader;

            unsafe {
                silverMaterial.Maps[(int)MaterialMapIndex.Metalness].Value = 0.8f;
                silverMaterial.Maps[(int)MaterialMapIndex.Roughness].Value = 0.8f;
                silverMaterial.Maps[(int)MaterialMapIndex.Occlusion].Value = 1.0f;

                // Apply corroded metal textures
                silverMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = silverAlbedoTexture;
                silverMaterial.Maps[(int)MaterialMapIndex.Metalness].Texture = silverMetalnessTexture;
                silverMaterial.Maps[(int)MaterialMapIndex.Normal].Texture = silverNormalTexture;
                silverMaterial.Maps[(int)MaterialMapIndex.Roughness].Texture = silverRoughnessTexture;

                // Set texture wrapping
                SetTextureWrapRepeat(silverMaterial);

                Console.WriteLine($"[DEBUG] Silver material created with shader ID: {silverMaterial.Shader.Id}");
            }
        }

        private void SetTextureWrapRepeat(Material material) {
            unsafe {
                Raylib.SetTextureWrap(material.Maps[(int)MaterialMapIndex.Albedo].Texture, TextureWrap.Repeat);
                Raylib.SetTextureWrap(material.Maps[(int)MaterialMapIndex.Metalness].Texture, TextureWrap.Repeat);
                Raylib.SetTextureWrap(material.Maps[(int)MaterialMapIndex.Normal].Texture, TextureWrap.Repeat);
                Raylib.SetTextureWrap(material.Maps[(int)MaterialMapIndex.Roughness].Texture, TextureWrap.Repeat);
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
                spinSpeed = Math.Clamp(spinSpeed, -maxSpinSpeed, maxSpinSpeed);
                lastMousePosition = currentMousePosition;
            }
            else if (Math.Abs(spinSpeed) > baseSpin) {
                float frictionRate = 2.0f; // How many "half-lives" per second
                float frictionFactor = (float)Math.Pow(0.5, frictionRate * Raylib.GetFrameTime());
                spinSpeed *= frictionFactor;
            }
        }

        public void Update(float deltaTime, Camera3D camera) {
            var cameraPosition = camera.Position;

            // Handle spin control input
            HandleSpinControl();

            // Update rotation based on current spin speed
            rotationAngle += spinSpeed * deltaTime;
            if (rotationAngle >= 360.0f) rotationAngle -= 360.0f;
            else if (rotationAngle < 0.0f) rotationAngle += 360.0f;

            lightDepth += Raylib.GetMouseWheelMove() * 0.3f;

            // Get mouse ray for light position
            Vector2 mousePosition = new Vector2(Raylib.GetMouseX(), Raylib.GetMouseY());
            Ray mouseRay = Raylib.GetMouseRay(mousePosition, camera);
            lightPosition = cameraPosition + mouseRay.Direction * lightDepth;

            // Update shader uniforms with correct uniform names
            int viewPosLoc = Raylib.GetShaderLocation(pbrShader, "viewPos");
            Raylib.SetShaderValue(pbrShader, viewPosLoc, new float[] { cameraPosition.X, cameraPosition.Y, cameraPosition.Z }, ShaderUniformDataType.Vec3);

            int lightPosLoc = Raylib.GetShaderLocation(pbrShader, "lightPos");
            Raylib.SetShaderValue(pbrShader, lightPosLoc, new float[] { lightPosition.X, lightPosition.Y, lightPosition.Z }, ShaderUniformDataType.Vec3);

            // FIXED: Create rotation matrix WITHOUT scale for lighting calculations
            Matrix4x4 rotationMatrix = Matrix4x4.CreateRotationY(rotationAngle * Raylib.DEG2RAD);

            // Use only rotation for the model matrix in shader (for lighting)
            int modelMatrixLoc = Raylib.GetShaderLocation(pbrShader, "matModel");
            Raylib.SetShaderValueMatrix(pbrShader, modelMatrixLoc, rotationMatrix);

            // Calculate normal matrix from rotation only (no scale interference)
            Matrix4x4 normalMatrix = Matrix4x4.Transpose(rotationMatrix);
            Matrix4x4.Invert(normalMatrix, out var inverted);
            int normalMatrixLoc = Raylib.GetShaderLocation(pbrShader, "matNormal");
            Raylib.SetShaderValueMatrix(pbrShader, normalMatrixLoc, inverted);

            int scaleLocation = Raylib.GetShaderLocation(pbrShader, "textureScale");
            float defaultScale = 0.4f;
            Raylib.SetShaderValue(pbrShader, scaleLocation, new float[] { defaultScale, defaultScale }, ShaderUniformDataType.Vec2);

            int colDiffuseLoc = Raylib.GetShaderLocation(pbrShader, "colDiffuse");
            Raylib.SetShaderValue(pbrShader, colDiffuseLoc, new float[] { 1, 1, 1, 1 }, ShaderUniformDataType.Vec4);
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

            // Calculate model dimensions
            Vector3 size = new Vector3(
                bounds.Max.X - bounds.Min.X,
                bounds.Max.Y - bounds.Min.Y,
                bounds.Max.Z - bounds.Min.Z
            );

            // Estimate scale to fit the model in the viewport
            float fovY = camera.FovY * Raylib.DEG2RAD;
            float fovX = 2.0f * MathF.Atan(MathF.Tan(fovY / 2.0f) * aspectRatio);
            float maxDimension = Math.Max(size.X, Math.Max(size.Y, size.Z));
            float distance = 5.0f;
            float scale = Math.Min(
                (distance * MathF.Tan(fovX / 2.0f)) / (size.X / 2.0f),
                (distance * MathF.Tan(fovY / 2.0f)) / (size.Y / 2.0f)
            ) * 0.9f;

            // Set gold texture scale before rendering logo
            int scaleLocation = Raylib.GetShaderLocation(pbrShader, "textureScale");
            float goldScale = .6f;
            Raylib.SetShaderValue(pbrShader, scaleLocation, new float[] { goldScale, goldScale }, ShaderUniformDataType.Vec2);

            // FIXED: Scale is applied only during rendering, not in shader uniforms
            // The shader uses rotation-only matrices for proper lighting
            Raylib.DrawModelEx(logoModel, new Vector3(0.0f, 0f, -3f),
                              new Vector3(0.0f, 1.0f, 0.0f), rotationAngle,
                              new Vector3(scale, scale, scale) * 2.0f, Color.Gold);

            // Set silver texture scale before rendering text models
            float silverScale = 0.5f;
            Raylib.SetShaderValue(pbrShader, scaleLocation, new float[] { silverScale, silverScale }, ShaderUniformDataType.Vec2);

            // Text models don't rotate, so use regular DrawModel
            Raylib.DrawModel(asheronsTextModel, new Vector3(0.0f, 0, 1.01f), scale, Color.White);
            Raylib.DrawModel(callTextModel, new Vector3(0.15f, 0, 1.0f), scale, Color.White);

        }

        public void Dispose() {
            Raylib.UnloadModel(logoModel);
            Raylib.UnloadModel(asheronsTextModel);
            Raylib.UnloadModel(callTextModel);

            // Unload shader
            Raylib.UnloadShader(pbrShader);

            // Unload materials
            Raylib.UnloadMaterial(goldMaterial);
            Raylib.UnloadMaterial(silverMaterial);

            // Unload gold textures
            Raylib.UnloadTexture(goldAlbedoTexture);
            Raylib.UnloadTexture(goldMetalnessTexture);
            Raylib.UnloadTexture(goldNormalTexture);
            Raylib.UnloadTexture(goldRoughnessTexture);
            Raylib.UnloadTexture(goldOcclusionTexture);

            // Unload silver textures
            Raylib.UnloadTexture(silverAlbedoTexture);
            Raylib.UnloadTexture(silverMetalnessTexture);
            Raylib.UnloadTexture(silverNormalTexture);
            Raylib.UnloadTexture(silverRoughnessTexture);
        }
    }
}