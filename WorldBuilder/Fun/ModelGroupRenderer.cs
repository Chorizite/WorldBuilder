using System.Numerics;
using Raylib_cs;
using System;
using System.Diagnostics;

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

            // Debug normals
            DebugModelNormals(logoModel, "Logo");
            DebugModelNormals(asheronsTextModel, "AsheronsText");
            DebugModelNormals(callTextModel, "CallText");

            // Load lighting shader
            lightingShader = Raylib.LoadShader("Resources/Shaders/lighting.vs", "Resources/Shaders/lighting.fs");

            // Create metallic material
            metallicMaterial = Raylib.LoadMaterialDefault();
            unsafe {
                metallicMaterial.Maps[(int)MaterialMapIndex.Albedo].Texture = Raylib.LoadTexture("Resources/Textures/diamond-metal.png");
                metallicMaterial.Maps[(int)MaterialMapIndex.Metalness].Value = 0f;
                metallicMaterial.Maps[(int)MaterialMapIndex.Roughness].Value = .05f;
                metallicMaterial.Shader = lightingShader;
                Console.WriteLine($"[DEBUG] Material shader ID: {metallicMaterial.Shader.Id}");
            }

            // Create metallic material
            metallicMaterial2 = Raylib.LoadMaterialDefault();
            unsafe {
                metallicMaterial2.Maps[(int)MaterialMapIndex.Albedo].Texture = Raylib.LoadTexture("Resources/Textures/images.jpg");
                metallicMaterial2.Maps[(int)MaterialMapIndex.Metalness].Value = 0f;
                metallicMaterial2.Maps[(int)MaterialMapIndex.Roughness].Value = .05f;
                metallicMaterial2.Shader = lightingShader;
                Console.WriteLine($"[DEBUG] Material shader ID: {metallicMaterial2.Shader.Id}");
            }

            // Apply material to all models
            ApplyMaterialToModel(ref logoModel, "Logo", metallicMaterial2);
            ApplyMaterialToModel(ref asheronsTextModel, "AsheronsText", metallicMaterial);
            ApplyMaterialToModel(ref callTextModel, "CallText", metallicMaterial);
            ApplyMaterialToModel(ref testCube, "TestCube", metallicMaterial);

            // Setup point light, view position, and debug mode
            unsafe {
                lightLoc = (int*)Raylib.MemAlloc(sizeof(int));
                viewPosLoc = (int*)Raylib.MemAlloc(sizeof(int));
                debugModeLoc = (int*)Raylib.MemAlloc(sizeof(int));
                *lightLoc = Raylib.GetShaderLocation(lightingShader, "lightPos");
                *viewPosLoc = Raylib.GetShaderLocation(lightingShader, "viewPos");
                *debugModeLoc = Raylib.GetShaderLocation(lightingShader, "debugMode");
                Console.WriteLine($"[DEBUG] Shader locations: lightPos={*lightLoc}, viewPos={*viewPosLoc}, debugMode={*debugModeLoc}");
                // Set initial light position
                lightPos = new Vector3(3.0f, 2.0f, 0.0f);
                float[] lightPosArray = new float[] { lightPos.X, lightPos.Y, lightPos.Z };
                fixed (float* posPtr = lightPosArray) {
                    Raylib.SetShaderValue(lightingShader, *lightLoc, posPtr, ShaderUniformDataType.Vec3);
                }
                // Set initial view position
                Vector3 viewPos = new Vector3(0.0f, 0.0f, 7.0f);
                float[] viewPosArray = new float[] { viewPos.X, viewPos.Y, viewPos.Z };
                fixed (float* viewPtr = viewPosArray) {
                    Raylib.SetShaderValue(lightingShader, *viewPosLoc, viewPtr, ShaderUniformDataType.Vec3);
                }
                // Set initial debug mode
                int debugModeValue = 0;
                Raylib.SetShaderValue(lightingShader, *debugModeLoc, &debugModeValue, ShaderUniformDataType.Int);
            }

            rotationAngle = 0.0f;
            lightAngle = 0.0f;
            debugMode = 0;
            useTestCube = false;
            useNormalFix = true; // Enable normal fix by default
            Console.WriteLine("[DEBUG] Normal Z-component fix enabled");
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
            // Toggle debug mode (N key cycles: 0 -> 1 -> 2 -> 0), test cube (C key), normal fix (F key)
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

            // Update model rotation
            rotationAngle += 45.0f * deltaTime;
            if (rotationAngle >= 360.0f) rotationAngle -= 360.0f;

            // Update light position (spherical orbit with more Z variation)
            lightAngle += 60.0f * deltaTime;
            if (lightAngle >= 360.0f) lightAngle -= 360.0f;
            float lightRadius = 3.0f;
            float lightHeight = 2.0f * MathF.Sin(MathF.PI * lightAngle / 90.0f);
            lightPos = new Vector3(
                lightRadius * MathF.Cos(MathF.PI * lightAngle / 180.0f),
                lightHeight,
                lightRadius * MathF.Sin(MathF.PI * lightAngle / 180.0f)
            );

            // Update shader uniforms
            unsafe {
                float[] lightPosArray = new float[] { lightPos.X, lightPos.Y, lightPos.Z };
                fixed (float* posPtr = lightPosArray) {
                    Raylib.SetShaderValue(lightingShader, *lightLoc, posPtr, ShaderUniformDataType.Vec3);
                }
                float[] viewPosArray = new float[] { cameraPosition.X, cameraPosition.Y, cameraPosition.Z };
                fixed (float* viewPtr = viewPosArray) {
                    Raylib.SetShaderValue(lightingShader, *viewPosLoc, viewPtr, ShaderUniformDataType.Vec3);
                }
            }
        }

        public void Render(Camera3D camera) {
            if (useTestCube) {
                Raylib.DrawModel(testCube, new Vector3(0.0f, 0.0f, 0.0f), 1.0f, Color.White);
            }
            else {
                Raylib.DrawModel(logoModel, new Vector3(0.0f, 0.0f, -2.0f), 1.0f, Color.Gold);
                Raylib.DrawModel(asheronsTextModel, new Vector3(0.0f, 0.0f, 1.0f), 1.0f, Color.White);
                Raylib.DrawModel(callTextModel, new Vector3(0.0f, 0.0f, 1.5f), 1.0f, Color.White);
            }

            // Debug: Draw light position
            Raylib.DrawSphere(lightPos, 0.1f, Color.Red);

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
            }
            Raylib.UnloadMaterial(metallicMaterial);
            Raylib.UnloadShader(lightingShader);
        }

        public bool UseNormalFix => useNormalFix; // Expose for shader
    }
}