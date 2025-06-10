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
        private Model testCube;
        private Material metallicMaterial;
        private float rotationAngle;
        private float lightAngle;
        private Shader lightingShader;
        private unsafe int* lightLoc;
        private unsafe int* viewPosLoc;
        private unsafe int* debugModeLoc;
        private unsafe int* timeLoc;
        private Vector3 lightPos;
        private int debugMode;
        private bool useTestCube;
        private bool useNormalFix;
        private Material metallicMaterial2;

        public ModelGroupRenderer() {
            // Load models
            logoModel = Raylib.LoadModel("Resources/Models/logo.obj");
            asheronsTextModel = Raylib.LoadModel("Resources/Models/asheronstext.obj");
            callTextModel = Raylib.LoadModel("Resources/Models/calltext.obj");
            var _testCube = Raylib.GenMeshCube(1.0f, 1.0f, 1.0f);
            testCube = Raylib.LoadModelFromMesh(_testCube);

            DebugModelNormals(logoModel, "Logo");
            DebugModelNormals(asheronsTextModel, "AsheronsText");
            DebugModelNormals(callTextModel, "CallText");

            lightingShader = Raylib.LoadShader("Resources/Shaders/lighting.vs", "Resources/Shaders/lighting.fs");

            metallicMaterial = Raylib.LoadMaterialDefault();
            unsafe {
                metallicMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = Raylib.LoadTexture("Resources/Textures/liquid-silver.jpg");
                Raylib.SetTextureWrap(metallicMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture, TextureWrap.MirrorRepeat);
                metallicMaterial.Maps[(int)MaterialMapIndex.Metalness].Value = 0.9f;
                metallicMaterial.Maps[(int)MaterialMapIndex.Roughness].Value = 0.05f;
                metallicMaterial.Shader = lightingShader;
                Console.WriteLine($"[DEBUG] Material shaderMożID: {metallicMaterial.Shader.Id}");
            }

            
            metallicMaterial2 = Raylib.LoadMaterialDefault();
            unsafe {
                metallicMaterial2.Maps[(int)MaterialMapIndex.Albedo].Texture = Raylib.LoadTexture("Resources/Textures/liquid-gold.jpg");
                Raylib.SetTextureWrap(metallicMaterial2.Maps[(int)MaterialMapIndex.Albedo].Texture, TextureWrap.MirrorRepeat);
                metallicMaterial2.Maps[(int)MaterialMapIndex.Metalness].Value = 0.9f;
                metallicMaterial2.Maps[(int)MaterialMapIndex.Roughness].Value = 0.05f;
                metallicMaterial2.Shader = lightingShader;
                Console.WriteLine($"[DEBUG] Material shader ID: {metallicMaterial2.Shader.Id}");
            }

            ApplyMaterialToModel(ref logoModel, "Logo", metallicMaterial2);
            ApplyMaterialToModel(ref asheronsTextModel, "AsheronsText", metallicMaterial);
            ApplyMaterialToModel(ref callTextModel, "CallText", metallicMaterial);
            ApplyMaterialToModel(ref testCube, "TestCube", metallicMaterial);

            unsafe {
                lightLoc = (int*)Raylib.MemAlloc(sizeof(int));
                viewPosLoc = (int*)Raylib.MemAlloc(sizeof(int));
                debugModeLoc = (int*)Raylib.MemAlloc(sizeof(int));
                timeLoc = (int*)Raylib.MemAlloc(sizeof(int));
                *lightLoc = Raylib.GetShaderLocation(lightingShader, "lightPos");
                *viewPosLoc = Raylib.GetShaderLocation(lightingShader, "viewPos");
                *debugModeLoc = Raylib.GetShaderLocation(lightingShader, "debugMode");
                *timeLoc = Raylib.GetShaderLocation(lightingShader, "time");
                Console.WriteLine($"[DEBUG] Shader locations: lightPos={*lightLoc}, viewPos={*viewPosLoc}, debugMode={*debugModeLoc}, time={*timeLoc}");
                
                lightPos = new Vector3(3.0f, 2.0f, 0.0f);
                float[] lightPosArray = new float[] { lightPos.X, lightPos.Y, lightPos.Z };
                fixed (float* posPtr = lightPosArray) {
                    Raylib.SetShaderValue(lightingShader, *lightLoc, posPtr, ShaderUniformDataType.Vec3);
                }
                
                Vector3 viewPos = new Vector3(0.0f, 0.0f, 7.0f);
                float[] viewPosArray = new float[] { viewPos.X, viewPos.Y, viewPos.Z };
                fixed (float* viewPtr = viewPosArray) {
                    Raylib.SetShaderValue(lightingShader, *viewPosLoc, viewPtr, ShaderUniformDataType.Vec3);
                }
                
                int debugModeValue = 0;
                Raylib.SetShaderValue(lightingShader, *debugModeLoc, &debugModeValue, ShaderUniformDataType.Int);
                
                float timeValue = 0.0f;
                Raylib.SetShaderValue(lightingShader, *timeLoc, &timeValue, ShaderUniformDataType.Float);
            }

            rotationAngle = 0.0f;
            lightAngle = 0.0f;
            debugMode = 0;
            useTestCube = false;
            useNormalFix = true;
        }

        private void ApplyMaterialToModel(ref Model model, string modelName, Material mat) {
            unsafe {
                for (int i = 0; i < model.MaterialCount; i++) {
                    model.Materials[i] = mat;
                    Console.WriteLine($"[DEBUG] {modelName}: Applied material with shader ID {mat.Shader.Id}");
                }
            }
        }

        private void DebugModelNormals(Model model, string modelName) {
            unsafe {
                if (model.MeshCount == 0) return;
                Mesh mesh = model.Meshes[0];
                if (mesh.Normals == null) {
                    Console.WriteLine($"[DEBUG] {modelName}: No vertex normals found in model.");
                    return;
                }
                float* normals = mesh.Normals;
                int vertexCount = mesh.VertexCount;
                Vector3 firstNormal = new Vector3(normals[0], normals[1], normals[2]);
                bool allSame = true;
                for (int i = 1; i < vertexCount; i++) {
                    Vector3 normal = new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]);
                    if (normal != firstNormal) {
                        allSame = false;
                        break;
                    }
                }
                Console.WriteLine($"[DEBUG] {modelName}: Normals count={vertexCount}, First normal={firstNormal}, All same={allSame}");
            }
        }

        public void Update(float deltaTime, Vector3 cameraPosition) {
            
            if (Raylib.IsKeyPressed(KeyboardKey.N)) {
                debugMode = (debugMode + 1) % 3;
                unsafe {
                    int debugModeValue = debugMode;
                    Raylib.SetShaderValue(lightingShader, *debugModeLoc, &debugModeValue, ShaderUniformDataType.Int);
                }
            }
            if (Raylib.IsKeyPressed(KeyboardKey.C)) {
                useTestCube = !useTestCube;
                Console.WriteLine($"[DEBUG] Test cube mode: {useTestCube}");
            }
            if (Raylib.IsKeyPressed(KeyboardKey.F)) {
                useNormalFix = !useNormalFix;
                Console.WriteLine($"[DEBUG] Normal Z-component fix: {useNormalFix}");
            }

            rotationAngle += 45.0f * deltaTime;
            if (rotationAngle >= 360.0f) rotationAngle -= 360.0f;

            lightAngle += 60.0f * deltaTime;
            if (lightAngle >= 360.0f) lightAngle -= 360.0f;
            float lightRadius = 3.0f;
            float lightHeight = 2.0f * MathF.Sin(MathF.PI * lightAngle / 90.0f);
            lightPos = new Vector3(
                lightRadius * MathF.Cos(MathF.PI * lightAngle / 180.0f),
                lightHeight,
                lightRadius * MathF.Sin(MathF.PI * lightAngle / 180.0f)
            );

            
            unsafe {
                float[] lightPosArray = new float[] { lightPos.X, lightPos.Y, lightPos.Z };
                fixed (float* posPtr = lightPosArray) {
                    Raylib.SetShaderValue(lightingShader, *lightLoc, posPtr, ShaderUniformDataType.Vec3);
                }
                float[] viewPosArray = new float[] { cameraPosition.X, cameraPosition.Y, cameraPosition.Z };
                fixed (float* viewPtr = viewPosArray) {
                    Raylib.SetShaderValue(lightingShader, *viewPosLoc, viewPtr, ShaderUniformDataType.Vec3);
                }
                
                float timeValue = (float)Raylib.GetTime();
                Raylib.SetShaderValue(lightingShader, *timeLoc, &timeValue, ShaderUniformDataType.Float);
            }
        }

        public void Render(Camera3D camera) {
            float screenWidth = Raylib.GetScreenWidth();
            float screenHeight = Raylib.GetScreenHeight();
            float aspectRatio = screenWidth / screenHeight;

            // Calculate the combined bounding box for all models (or use testCube)
            BoundingBox? bounds = null;
            if (useTestCube) {
                bounds = Raylib.GetModelBoundingBox(testCube);
            }
            else {
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
                bounds = new BoundingBox(min, max);
            }

            // Calculate model dimensions
            Vector3 size = new Vector3(
                bounds.Value.Max.X - bounds.Value.Min.X,
                bounds.Value.Max.Y - bounds.Value.Min.Y,
                bounds.Value.Max.Z - bounds.Value.Min.Z
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

            // Apply scale and render models
            if (useTestCube) {
                Raylib.DrawModel(testCube, new Vector3(0.0f, 0.0f, 0.0f), scale, Color.White);
            }
            else {
                Raylib.DrawModel(logoModel, new Vector3(0.0f, 0.0f, -2.0f), scale, Color.Gold);
                Raylib.DrawModel(asheronsTextModel, new Vector3(0.0f, 0.0f, 1.1f), scale, Color.White);
                Raylib.DrawModel(callTextModel, new Vector3(0.05f, 0.0f, 1.0f), scale, Color.White);
            }

            // Debug: Draw light position
            //Raylib.DrawSphere(lightPos, 0.1f * scale, Color.Red);

            // Debug: Log sample normal
            unsafe {
                Mesh mesh = logoModel.Meshes[0];
                if (mesh.Normals != null) {
                    float* normals = mesh.Normals;
                    Vector3 sampleNormal = new Vector3(normals[0], normals[1], normals[2]);
                    //Console.WriteLine($"[DEBUG] Logo render-time normal sample: {sampleNormal}");
                }
            }
        }

        public void Dispose() {
            Raylib.UnloadModel(logoModel);
            Raylib.UnloadModel(asheronsTextModel);
            Raylib.UnloadModel(callTextModel);
            Raylib.UnloadModel(testCube);
            unsafe {
                Raylib.UnloadTexture(metallicMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture);
                Raylib.MemFree(lightLoc);
                Raylib.MemFree(viewPosLoc);
                Raylib.MemFree(debugModeLoc);
                Raylib.MemFree(timeLoc); // Free time uniform memory
            }
            Raylib.UnloadMaterial(metallicMaterial);
            Raylib.UnloadShader(lightingShader);
        }

        public bool UseNormalFix => useNormalFix;
    }
}