using System.Runtime.InteropServices;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SysVec2 = System.Numerics.Vector2;
using SysVec4 = System.Numerics.Vector4;

namespace CloudScope.Platform.OpenGL
{
    public sealed class ImGuiController : IDisposable
    {
        private int _vertexArray;
        private int _vertexBuffer;
        private int _indexBuffer;
        private int _fontTexture;
        private int _shader;
        private int _attribLocationTex;
        private int _attribLocationProjMtx;
        private int _attribLocationVtxPos;
        private int _attribLocationVtxUV;
        private int _attribLocationVtxColor;
        private int _vertexBufferSize;
        private int _indexBufferSize;

        public ImGuiController(int width, int height)
        {
            ImGui.CreateContext();
            ImGui.StyleColorsDark();

            ImGuiIOPtr io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            io.DisplaySize = new SysVec2(width, height);

            CreateDeviceResources();
        }

        public void Update(GameWindow window, float deltaSeconds)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new SysVec2(window.ClientSize.X, window.ClientSize.Y);
            if (window.ClientSize.X > 0 && window.ClientSize.Y > 0)
            {
                io.DisplayFramebufferScale = new SysVec2(
                    (float)window.FramebufferSize.X / window.ClientSize.X,
                    (float)window.FramebufferSize.Y / window.ClientSize.Y);
            }

            io.DeltaTime = deltaSeconds > 0f ? deltaSeconds : 1f / 60f;
            UpdateInput(window);

            ImGui.NewFrame();
        }

        public void PressChar(uint codepoint)
        {
            ImGui.GetIO().AddInputCharacter(codepoint);
        }

        public void Render()
        {
            ImGui.Render();
            RenderDrawData(ImGui.GetDrawData());
        }

        public bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;
        public bool WantsMouse => ImGui.GetIO().WantCaptureMouse;

        private void CreateDeviceResources()
        {
            _vertexBufferSize = 10_000;
            _indexBufferSize = 2_000;

            _vertexBuffer = GL.GenBuffer();
            _indexBuffer = GL.GenBuffer();
            _vertexArray = GL.GenVertexArray();

            RecreateFontDeviceTexture();

            const string vertexSource = @"#version 330 core
uniform mat4 projection_matrix;
layout (location = 0) in vec2 in_position;
layout (location = 1) in vec2 in_texCoord;
layout (location = 2) in vec4 in_color;
out vec2 frag_UV;
out vec4 frag_Color;
void main()
{
    frag_UV = in_texCoord;
    frag_Color = in_color;
    gl_Position = projection_matrix * vec4(in_position, 0, 1);
}";

            const string fragmentSource = @"#version 330 core
uniform sampler2D in_fontTexture;
in vec2 frag_UV;
in vec4 frag_Color;
out vec4 output_color;
void main()
{
    output_color = frag_Color * texture(in_fontTexture, frag_UV.st);
}";

            _shader = CreateProgram(vertexSource, fragmentSource);
            _attribLocationTex = GL.GetUniformLocation(_shader, "in_fontTexture");
            _attribLocationProjMtx = GL.GetUniformLocation(_shader, "projection_matrix");
            _attribLocationVtxPos = 0;
            _attribLocationVtxUV = 1;
            _attribLocationVtxColor = 2;

            GL.BindVertexArray(_vertexArray);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);

            int stride = Marshal.SizeOf<ImDrawVert>();
            GL.EnableVertexAttribArray(_attribLocationVtxPos);
            GL.EnableVertexAttribArray(_attribLocationVtxUV);
            GL.EnableVertexAttribArray(_attribLocationVtxColor);
            GL.VertexAttribPointer(_attribLocationVtxPos, 2, VertexAttribPointerType.Float, false, stride, 0);
            GL.VertexAttribPointer(_attribLocationVtxUV, 2, VertexAttribPointerType.Float, false, stride, 8);
            GL.VertexAttribPointer(_attribLocationVtxColor, 4, VertexAttribPointerType.UnsignedByte, true, stride, 16);

            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
        }

        private void RecreateFontDeviceTexture()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

            int previousTexture = GL.GetInteger(GetPName.TextureBinding2D);
            _fontTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _fontTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            io.Fonts.SetTexID((IntPtr)_fontTexture);
            GL.BindTexture(TextureTarget.Texture2D, previousTexture);
        }

        private static void UpdateInput(GameWindow window)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            MouseState mouse = window.MouseState;
            KeyboardState keyboard = window.KeyboardState;

            io.AddMouseButtonEvent(0, mouse.IsButtonDown(MouseButton.Left));
            io.AddMouseButtonEvent(1, mouse.IsButtonDown(MouseButton.Right));
            io.AddMouseButtonEvent(2, mouse.IsButtonDown(MouseButton.Middle));
            io.AddMousePosEvent(mouse.Position.X, mouse.Position.Y);
            io.AddMouseWheelEvent(mouse.ScrollDelta.X, mouse.ScrollDelta.Y);

            AddKey(io, ImGuiKey.Tab, keyboard, Keys.Tab);
            AddKey(io, ImGuiKey.LeftArrow, keyboard, Keys.Left);
            AddKey(io, ImGuiKey.RightArrow, keyboard, Keys.Right);
            AddKey(io, ImGuiKey.UpArrow, keyboard, Keys.Up);
            AddKey(io, ImGuiKey.DownArrow, keyboard, Keys.Down);
            AddKey(io, ImGuiKey.PageUp, keyboard, Keys.PageUp);
            AddKey(io, ImGuiKey.PageDown, keyboard, Keys.PageDown);
            AddKey(io, ImGuiKey.Home, keyboard, Keys.Home);
            AddKey(io, ImGuiKey.End, keyboard, Keys.End);
            AddKey(io, ImGuiKey.Delete, keyboard, Keys.Delete);
            AddKey(io, ImGuiKey.Backspace, keyboard, Keys.Backspace);
            AddKey(io, ImGuiKey.Enter, keyboard, Keys.Enter);
            AddKey(io, ImGuiKey.Escape, keyboard, Keys.Escape);
            AddKey(io, ImGuiKey.A, keyboard, Keys.A);
            AddKey(io, ImGuiKey.C, keyboard, Keys.C);
            AddKey(io, ImGuiKey.V, keyboard, Keys.V);
            AddKey(io, ImGuiKey.X, keyboard, Keys.X);
            AddKey(io, ImGuiKey.Y, keyboard, Keys.Y);
            AddKey(io, ImGuiKey.Z, keyboard, Keys.Z);
            io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
            io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));
            io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
            io.AddKeyEvent(ImGuiKey.ModSuper, keyboard.IsKeyDown(Keys.LeftSuper) || keyboard.IsKeyDown(Keys.RightSuper));
        }

        private static void AddKey(ImGuiIOPtr io, ImGuiKey imguiKey, KeyboardState keyboard, Keys key)
            => io.AddKeyEvent(imguiKey, keyboard.IsKeyDown(key));

        private void RenderDrawData(ImDrawDataPtr drawData)
        {
            int framebufferWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
            int framebufferHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
            if (framebufferWidth <= 0 || framebufferHeight <= 0)
                return;

            drawData.ScaleClipRects(drawData.FramebufferScale);

            GL.GetInteger(GetPName.ActiveTexture, out int lastActiveTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.GetInteger(GetPName.CurrentProgram, out int lastProgram);
            GL.GetInteger(GetPName.TextureBinding2D, out int lastTexture);
            GL.GetInteger(GetPName.ArrayBufferBinding, out int lastArrayBuffer);
            GL.GetInteger(GetPName.VertexArrayBinding, out int lastVertexArray);
            GL.GetInteger(GetPName.BlendSrcRgb, out int lastBlendSrcRgb);
            GL.GetInteger(GetPName.BlendDstRgb, out int lastBlendDstRgb);
            GL.GetInteger(GetPName.BlendSrcAlpha, out int lastBlendSrcAlpha);
            GL.GetInteger(GetPName.BlendDstAlpha, out int lastBlendDstAlpha);
            bool lastBlend = GL.IsEnabled(EnableCap.Blend);
            bool lastCullFace = GL.IsEnabled(EnableCap.CullFace);
            bool lastDepthTest = GL.IsEnabled(EnableCap.DepthTest);
            bool lastScissorTest = GL.IsEnabled(EnableCap.ScissorTest);

            GL.Enable(EnableCap.Blend);
            GL.BlendEquation(BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ScissorTest);

            GL.Viewport(0, 0, framebufferWidth, framebufferHeight);
            Matrix4 projection = Matrix4.CreateOrthographicOffCenter(
                drawData.DisplayPos.X,
                drawData.DisplayPos.X + drawData.DisplaySize.X,
                drawData.DisplayPos.Y + drawData.DisplaySize.Y,
                drawData.DisplayPos.Y,
                -1f,
                1f);

            GL.UseProgram(_shader);
            GL.Uniform1(_attribLocationTex, 0);
            GL.UniformMatrix4(_attribLocationProjMtx, false, ref projection);
            GL.BindVertexArray(_vertexArray);

            for (int n = 0; n < drawData.CmdListsCount; n++)
            {
                ImDrawListPtr cmdList = drawData.CmdLists[n];
                int vertexSize = cmdList.VtxBuffer.Size * Marshal.SizeOf<ImDrawVert>();
                if (vertexSize > _vertexBufferSize)
                {
                    _vertexBufferSize = (int)Math.Max(_vertexBufferSize * 1.5f, vertexSize);
                    GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
                    GL.BufferData(BufferTarget.ArrayBuffer, _vertexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                }

                int indexSize = cmdList.IdxBuffer.Size * sizeof(ushort);
                if (indexSize > _indexBufferSize)
                {
                    _indexBufferSize = (int)Math.Max(_indexBufferSize * 1.5f, indexSize);
                    GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
                    GL.BufferData(BufferTarget.ElementArrayBuffer, _indexBufferSize, IntPtr.Zero, BufferUsageHint.DynamicDraw);
                }

                GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertexSize, cmdList.VtxBuffer.Data);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indexBuffer);
                GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, indexSize, cmdList.IdxBuffer.Data);

                for (int cmdIndex = 0; cmdIndex < cmdList.CmdBuffer.Size; cmdIndex++)
                {
                    ImDrawCmdPtr drawCmd = cmdList.CmdBuffer[cmdIndex];
                    if (drawCmd.UserCallback != IntPtr.Zero)
                        throw new NotImplementedException("ImGui user callbacks are not supported.");

                    GL.BindTexture(TextureTarget.Texture2D, (int)drawCmd.TextureId);
                    SysVec4 clip = drawCmd.ClipRect;
                    GL.Scissor(
                        (int)clip.X,
                        (int)(framebufferHeight - clip.W),
                        (int)(clip.Z - clip.X),
                        (int)(clip.W - clip.Y));

                    GL.DrawElementsBaseVertex(
                        PrimitiveType.Triangles,
                        (int)drawCmd.ElemCount,
                        DrawElementsType.UnsignedShort,
                        (IntPtr)(drawCmd.IdxOffset * sizeof(ushort)),
                        (int)drawCmd.VtxOffset);
                }
            }

            if (lastScissorTest) GL.Enable(EnableCap.ScissorTest); else GL.Disable(EnableCap.ScissorTest);
            if (lastDepthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
            if (lastCullFace) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            if (lastBlend) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
            GL.BlendFuncSeparate(
                (BlendingFactorSrc)lastBlendSrcRgb,
                (BlendingFactorDest)lastBlendDstRgb,
                (BlendingFactorSrc)lastBlendSrcAlpha,
                (BlendingFactorDest)lastBlendDstAlpha);
            GL.UseProgram(lastProgram);
            GL.BindTexture(TextureTarget.Texture2D, lastTexture);
            GL.BindBuffer(BufferTarget.ArrayBuffer, lastArrayBuffer);
            GL.BindVertexArray(lastVertexArray);
            GL.ActiveTexture((TextureUnit)lastActiveTexture);
        }

        private static int CreateProgram(string vertexSource, string fragmentSource)
        {
            int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
            int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
            int program = GL.CreateProgram();
            GL.AttachShader(program, vertex);
            GL.AttachShader(program, fragment);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
                throw new InvalidOperationException(GL.GetProgramInfoLog(program));

            GL.DetachShader(program, vertex);
            GL.DetachShader(program, fragment);
            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
            return program;
        }

        private static int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
                throw new InvalidOperationException(GL.GetShaderInfoLog(shader));

            return shader;
        }

        public void Dispose()
        {
            GL.DeleteBuffer(_vertexBuffer);
            GL.DeleteBuffer(_indexBuffer);
            GL.DeleteVertexArray(_vertexArray);
            GL.DeleteTexture(_fontTexture);
            GL.DeleteProgram(_shader);
            ImGui.DestroyContext();
        }
    }
}
