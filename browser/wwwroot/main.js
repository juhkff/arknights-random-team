// WebAssembly 启动脚本。AppBuilder 由 .NET 侧提供，这里只负责拉起运行时并进入托管 Main。
//
// 运行时用动态 import 加载，而不是顶层的静态 import：
// 静态 import 失败时，本文件里后面的 try/catch 根本不会执行，加载遮罩会一直停住、
// 重试按钮也注册不上（实测：注入 dotnet.js 下载失败，超时与错误态都没被触发）。
// 宿主页在 main.js 之前已安装超时、资源加载失败与重试按钮的兜底，这里只负责通报状态。

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const hint = () => document.getElementById("app-loading-hint");

function showStartupError(message) {
    if (typeof globalThis.arknightsStartupError === "function") {
        globalThis.arknightsStartupError(message);
        return;
    }

    console.error(message);
}

// 托管侧在主视图加载完成后调用（AppHost.ShellReady → arknightsAppReady）。
// 撤罩与停表由宿主页的启动兜底统一处理，保证「入口模块失败」与「运行时失败」两条路径一致。
globalThis.arknightsAppReady = () => {
    if (typeof globalThis.arknightsStartupReady === "function") {
        globalThis.arknightsStartupReady();
        return;
    }

    document.getElementById("app-loading")?.classList.add("hidden");
};

try {
    const text = hint();
    if (text) text.textContent = "正在下载运行时…";

    const { dotnet } = await import("./_framework/dotnet.js");

    const dotnetRuntime = await dotnet
        .withDiagnosticTracing(false)
        .withApplicationArgumentsFromQuery()
        .create();

    const starting = hint();
    if (starting) starting.textContent = "正在启动应用…";

    const config = dotnetRuntime.getConfig();
    await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
} catch (err) {
    const detail = err?.message || String(err);
    showStartupError(`运行时启动失败。${detail}`);
    throw err;
}
