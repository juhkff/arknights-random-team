// WebAssembly 启动脚本。AppBuilder 由 .NET 侧提供，这里只负责拉起运行时并进入托管 Main。
import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

function showStartupError(message) {
    const overlay = document.getElementById("app-loading");
    const hint = document.getElementById("app-loading-hint");
    if (!overlay || !hint) {
        console.error(message);
        return;
    }

    overlay.classList.remove("hidden");
    overlay.classList.add("error");
    hint.textContent = message;
}

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
    const detail = err?.message || String(err);
    showStartupError(`运行时启动失败。${detail}`);
    throw err;
}
