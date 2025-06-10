import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const { getAssemblyExports, getConfig, runMain } = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);

const canvas = document.getElementById('canvas');
dotnet.instance.Module['canvas'] = canvas;

window.dotnet = dotnet;

function resetGLState() {

    const gl = dotnet.instance.Module.GL.contexts[1].GLctx;
    // Enable states expected by raylib
    gl.enable(gl.BLEND);
    gl.enable(gl.POINT_SIZE);
    gl.enable(gl.MULTISAMPLE);
    gl.enable(gl.DITHER);
    gl.frontFace(gl.CW);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 4);
    gl.drawBuffers([gl.BACK]);
    gl.depthMask(true);

    // Disable states modified by Skia
    gl.disable(gl.FRAMEBUFFER_SRGB);
    gl.disable(gl.SCISSOR_TEST);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

    // Restore buffer bindings
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);
    gl.bindBuffer(gl.ARRAY_BUFFER, null);
    gl.bindFramebuffer(gl.READ_FRAMEBUFFER, null);
    gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, null);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null);

    // Restore texture and sampler bindings
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, null);
    gl.bindSampler(0, null);
    gl.activeTexture(gl.TEXTURE31);
    gl.bindTexture(gl.TEXTURE_2D, null);
    gl.activeTexture(gl.TEXTURE0);

    // Ensure clean shader state
    gl.useProgram(null);

    // Optional: Basic render to verify setup (clear canvas to a color)
    gl.clearColor(0.0, 0.0, 0.0, 1.0);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
}

function mainLoop() {
    exports.Program.Update(window.innerWidth, window.innerHeight);
    resetGLState();
    exports.Program.Render();
    window.requestAnimationFrame(mainLoop);
}

await runMain(config.mainAssemblyName, [globalThis.location.href]);
window.requestAnimationFrame(mainLoop);
