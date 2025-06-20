﻿using Avalonia;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Lib.Renderer;
using WorldBuilder.Lib;
using Avalonia.Controls;
using Avalonia.Media;
using WorldBuilder.Lib.Avalonia;
using Evergine.Bindings.OpenGL;

namespace WorldBuilder {
    unsafe public class MainWindowGLRendering : BaseTkOpenGlControl {
        System.Numerics.Vector4 _color;

        // Create the vertices for our triangle. These are listed in normalized device coordinates (NDC)
        // In NDC, (0, 0) is the center of the screen.
        // Negative X coordinates move to the left, positive X move to the right.
        // Negative Y coordinates move to the bottom, positive Y move to the top.
        // OpenGL only supports rendering in 3D, so to create a flat triangle, the Z coordinate will be kept as 0.
        private readonly float[] _vertices =
        {
            -0.5f, -0.5f, 0.0f, // Bottom-left vertex
             0.5f, -0.5f, 0.0f, // Bottom-right vertex
             0.0f,  0.5f, 0.0f  // Top vertex
        };

        // These are the handles to OpenGL objects. A handle is an integer representing where the object lives on the
        // graphics card. Consider them sort of like a pointer; we can't do anything with them directly, but we can
        // send them to OpenGL functions that need them.

        // What these objects are will be explained in OnLoad.
        private uint _vertexBufferObject;

        private uint _vertexArrayObject;

        // This class is a wrapper around a shader, which helps us manage it.
        // The shader class's code is in the Common project.
        // What shaders are and what they're used for will be explained later in this tutorial.
        private AShader _shader;


        // Handle Avalonia Color Picker property and update 

        public static readonly StyledProperty<Color> SelectedColorProperty = AvaloniaProperty.Register<MainWindowGLRendering, Color>(nameof(SelectedColor));

        public Color SelectedColor {
            get => GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public MainWindowGLRendering() {

        }



        // OpenTkInit (OnLoad) is called once when the control is created
        protected override void OpenTkInit() {
            // This will be the color of the background after we clear it, in normalized colors.
            // Normalized colors are mapped on a range of 0.0 to 1.0, with 0.0 representing black, and 1.0 representing
            // the largest possible value for that channel.
            // This is a deep green.
            GL.glClearColor(0.2f, 0.3f, 0.3f, 1.0f);

            // We need to send our vertices over to the graphics card so OpenGL can use them.
            // To do this, we need to create what's called a Vertex Buffer Object (VBO).
            // These allow you to upload a bunch of data to a buffer, and send the buffer to the graphics card.
            // This effectively sends all the vertices at the same time.

            // First, we need to create a buffer. This function returns a handle to it, but as of right now, it's empty.
            fixed (uint* ptr = &_vertexBufferObject) {
                GL.glGenBuffers(1, ptr);
            }

            // Now, bind the buffer. OpenGL uses one global state, so after calling this,
            // all future calls that modify the VBO will be applied to this buffer until another buffer is bound instead.
            // The first argument is an enum, specifying what type of buffer we're binding. A VBO is an ArrayBuffer.
            // There are multiple types of buffers, but for now, only the VBO is necessary.
            // The second argument is the handle to our buffer.
            GL.glBindBuffer(BufferTargetARB.ArrayBuffer, _vertexBufferObject);

            // Finally, upload the vertices to the buffer.
            // Arguments:
            //   Which buffer the data should be sent to.
            //   How much data is being sent, in bytes. You can generally set this to the length of your array, multiplied by sizeof(array type).
            //   The vertices themselves.
            //   How the buffer will be used, so that OpenGL can write the data to the proper memory space on the GPU.
            //   There are three different BufferUsageHints for drawing:
            //     StaticDraw: This buffer will rarely, if ever, update after being initially uploaded.
            //     DynamicDraw: This buffer will change frequently after being initially uploaded.
            //     StreamDraw: This buffer will change on every frame.
            //   Writing to the proper memory space is important! Generally, you'll only want StaticDraw,
            //   but be sure to use the right one for your use case.
            fixed (float* ptr = _vertices) {
                GL.glBufferData(BufferTargetARB.ArrayBuffer, _vertices.Length * sizeof(float), (void*)ptr, BufferUsageARB.StaticDraw);
            }

            // One notable thing about the buffer we just loaded data into is that it doesn't have any structure to it. It's just a bunch of floats (which are actaully just bytes).
            // The opengl driver doesn't know how this data should be interpreted or how it should be divided up into vertices. To do this opengl introduces the idea of a 
            // Vertex Array Obejct (VAO) which has the job of keeping track of what parts or what buffers correspond to what data. In this example we want to set our VAO up so that 
            // it tells opengl that we want to interpret 12 bytes as 3 floats and divide the buffer into vertices using that.
            // To do this we generate and bind a VAO (which looks deceptivly similar to creating and binding a VBO, but they are different!).
            fixed (uint* ptr = &_vertexArrayObject) {
                GL.glGenVertexArrays(1, ptr);
            }
            GL.glBindVertexArray(_vertexArrayObject);

            // Now, we need to setup how the vertex shader will interpret the VBO data; you can send almost any C datatype (and a few non-C ones too) to it.
            // While this makes them incredibly flexible, it means we have to specify how that data will be mapped to the shader's input variables.

            // To do this, we use the GL.VertexAttribPointer function
            // This function has two jobs, to tell opengl about the format of the data, but also to associate the current array buffer with the VAO.
            // This means that after this call, we have setup this attribute to source data from the current array buffer and interpret it in the way we specified.
            // Arguments:
            //   Location of the input variable in the shader. the layout(location = 0) line in the vertex shader explicitly sets it to 0.
            //   How many elements will be sent to the variable. In this case, 3 floats for every vertex.
            //   The data type of the elements set, in this case float.
            //   Whether or not the data should be converted to normalized device coordinates. In this case, false, because that's already done.
            //   The stride; this is how many bytes are between the last element of one vertex and the first element of the next. 3 * sizeof(float) in this case.
            //   The offset; this is how many bytes it should skip to find the first element of the first vertex. 0 as of right now.
            // Stride and Offset are just sort of glossed over for now, but when we get into texture coordinates they'll be shown in better detail.
            GL.glVertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);

            // Enable variable 0 in the shader.
            GL.glEnableVertexAttribArray(0);

            // We've got the vertices done, but how exactly should this be converted to pixels for the final image?
            // Modern OpenGL makes this pipeline very free, giving us a lot of freedom on how vertices are turned to pixels.
            // The drawback is that we actually need two more programs for this! These are called "shaders".
            // Shaders are tiny programs that live on the GPU. OpenGL uses them to handle the vertex-to-pixel pipeline.
            // Check out the Shader class in Common to see how we create our shaders, as well as a more in-depth explanation of how shaders work.
            // shader.vert and shader.frag contain the actual shader code.
            _shader = new GLSLShader("shader");

            // Now, enable the shader.
            // Just like the VBO, this is global, so every function that uses a shader will modify this one until a new one is bound instead.
            _shader.SetActive();

            // Setup is now complete! Now we move to the OpenTkRender function to finally draw the triangle.

        }



        //OpenTkRender (OnRenderFrame) is called once a frame. The aspect ratio and keyboard state are configured prior to this being called.
        protected override void OpenTkRender() {
            _color = new System.Numerics.Vector4(SelectedColor.R / 255f, SelectedColor.G / 255f, SelectedColor.B / 255f, SelectedColor.A / 255f);

            GL.glDisable(EnableCap.DepthTest);
            GL.glDisable(EnableCap.CullFace);

            GL.glClearColor(0, 32 / 255f, 48 / 255f, 1);

            // This clears the image, using what you set as GL.ClearColor earlier.
            // OpenGL provides several different types of data that can be rendered.
            // You can clear multiple buffers by using multiple bit flags.
            // However, we only modify the color, so ColorBufferBit is all we need to clear.
            //GL.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);

            /*ImGui.Begin("Color chooser");
            ImGui.ColorEdit3("Triangle color", ref _color);
            ImGui.End();*/

            // To draw an object in OpenGL, it's typically as simple as binding your shader,
            // setting shader uniforms (not done here, will be shown in a future tutorial)
            // binding the VAO,
            // and then calling an OpenGL function to render.

            // Bind the shader
            _shader.SetActive();

            // Change triangle color
            _shader.SetUniform("ourColor", _color);

            // Bind the VAO
            GL.glBindVertexArray(_vertexArrayObject);

            // And then call our drawing function.
            // For this tutorial, we'll use GL.DrawArrays, which is a very simple rendering function.
            // Arguments:
            //   Primitive type; What sort of geometric primitive the vertices represent.
            //     OpenGL used to support many different primitive types, but almost all of the ones still supported
            //     is some variant of a triangle. Since we just want a single triangle, we use Triangles.
            //   Starting index; this is just the start of the data you want to draw. 0 here.
            //   How many vertices you want to draw. 3 for a triangle.
            GL.glDrawArrays(PrimitiveType.Triangles, 0, 3);

            // OpenTK windows are what's known as "double-buffered". In essence, the window manages two buffers.
            // One is rendered to while the other is currently displayed by the window.
            // This avoids screen tearing, a visual artifact that can happen if the buffer is modified while being displayed.
            // After drawing, call this function to swap the buffers. If you don't, it won't display what you've rendered.
            GL.glFlush();

            //Clean up the opengl state back to how we got it
            GL.glDisable(EnableCap.DepthTest);

        }

        //OpenTkTeardown is called when the control is being destroyed
        protected override void OpenTkTeardown() {
            Console.WriteLine("UI: Tearing down gl component");

        }
    }
}
