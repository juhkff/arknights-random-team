// WebAssembly 启动脚本。AppBuilder 由 .NET 侧提供，这里只负责拉起运行时并进入托管 Main。
import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const loadingOverlay = () => document.getElementById("app-loading");

function hideLoading() {
    loadingOverlay()?.classList.add("hidden");
}

function showStartupError(message) {
    const overlay = loadingOverlay();
    const hint = document.getElementById("app-loading-hint");
    if (!overlay || !hint) {
        console.error(message);
        return;
    }

    overlay.classList.remove("hidden");
    overlay.classList.add("error");
    hint.textContent = message;
}

// 启动时间超出预期时的兜底：给出重试入口，而不是一直停在「正在启动应用…」。
// 这里不猜进度，只说明「比预期久」；应用随后就绪时遮罩照样会被撤掉。
const startupTimer = setTimeout(
    () => showStartupError("启动时间超出预期。可以继续等待，或点「重试」重新加载。"),
    45000);

// 托管侧在主视图加载完成后调用（AppHost.ShellReady → arknightsAppReady）。
// 等到下一帧再撤遮罩，避免撤早了先露出空白画布。
globalThis.arknightsAppReady = () => {
    clearTimeout(startupTimer);
    requestAnimationFrame(hideLoading);
};

const hint = document.getElementById("app-loading-hint");
if (hint) hint.textContent = "正在下载运行时…";

try {
    const dotnetRuntime = await dotnet
        .withDiagnosticTracing(false)
        .withApplicationArgumentsFromQuery()
        .create();

    if (hint) hint.textContent = "正在启动应用…";

    const config = dotnetRuntime.getConfig();
    await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
} catch (err) {
    clearTimeout(startupTimer);
    const detail = err?.message || String(err);
    showStartupError(`运行时启动失败。${detail}`);
    throw err;
}
