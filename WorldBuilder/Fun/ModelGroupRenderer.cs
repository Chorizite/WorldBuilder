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
        private float lightArcTime; // Time for light arc animation

        public ModelGroupRenderer() {
            // Load models
            logoModel = Raylib.LoadModel("Resources/Models/logo.obj");
            asheronsTextModel = Raylib.LoadModel("Resources/Models/asheronstext.obj");
            callTextModel = Raylib.LoadModel("Resources/Models/calltext.obj");

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

        public void Update(float deltaTime, Vector3 cameraPosition) {
            rotationAngle += 45.0f * deltaTime;
            if (rotationAngle >= 360.0f) rotationAngle -= 360.0f;

            // Update light arc animation
            lightArcTime += deltaTime * 1.1f; // Adjust speed as needed
            if (lightArcTime >= 2.0f * MathF.PI) lightArcTime -= 2.0f * MathF.PI;

            // Calculate light position in an arc
            float arcRadius = 12.0f;
            float baseHeight = 1.0f; 
            float arcHeight = 3f;

            var lightPosition = new Vector3(
                arcRadius * MathF.Sin(lightArcTime),
                baseHeight + arcHeight * MathF.Sin(lightArcTime * 0.5f),
                3.6f
            );

            // Update shader uniforms
            int viewPosLoc = Raylib.GetShaderLocation(pbrShader, "viewPos");
            Raylib.SetShaderValue(pbrShader, viewPosLoc, new float[] { cameraPosition.X, cameraPosition.Y, cameraPosition.Z }, ShaderUniformDataType.Vec3);

            int lightPosLoc = Raylib.GetShaderLocation(pbrShader, "lightPos");
            Raylib.SetShaderValue(pbrShader, lightPosLoc, new float[] { lightPosition.X, lightPosition.Y, lightPosition.Z }, ShaderUniformDataType.Vec3);

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

            Vector3 rotationAxis = new Vector3(0.0f, 1.0f, 0.0f);
            Matrix4x4 rotationMatrix = Matrix4x4.CreateRotationY(rotationAngle * Raylib.DEG2RAD);

            int scaleLocation = Raylib.GetShaderLocation(pbrShader, "textureScale");
            float goldScale = .6f;
            Raylib.SetShaderValue(pbrShader, scaleLocation, new float[] { goldScale, goldScale }, ShaderUniformDataType.Vec2);

            logoModel.Transform = rotationMatrix;
            Raylib.DrawModel(logoModel, new Vector3(0.0f, -15.0f, -100.0f), scale * 20, Color.Gold);

            // Set silver texture scale before rendering text models
            float silverScale = 0.5f;
            Raylib.SetShaderValue(pbrShader, scaleLocation, new float[] { silverScale, silverScale }, ShaderUniformDataType.Vec2);

            Raylib.DrawModel(asheronsTextModel, new Vector3(0.0f, .38f, 1.1f), scale, Color.White);
            Raylib.DrawModel(callTextModel, new Vector3(0.15f, 0.38f, 1.0f), scale, Color.White);
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