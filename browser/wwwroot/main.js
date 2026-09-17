// WebAssembly 启动脚本。AppBuilder 由 .NET 侧提供，这里负责：
// 1. 尽量显示运行时下载进度
// 2. 用 Cache API 缓存立绘 CDN（不缓存干员名单 / 策略）
// 3. 拉起托管 Main
import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const ART_CACHE = "arknights-art-v1";
const ART_HOSTS = ["gcore.jsdelivr.net", "cdn.jsdelivr.net", "testingcf.jsdelivr.net"];

const splash = document.getElementById("app-loading");
const fill = document.getElementById("app-loading-fill");
const status = document.getElementById("app-loading-status");

function setStatus(text) {
    if (status) status.textContent = text;
}

function setProgress(loaded, total) {
    if (!(total > 0) || !fill || !splash) return;
    const pct = Math.max(0, Math.min(100, Math.round((loaded / total) * 100)));
    splash.classList.add("has-progress");
    fill.style.width = `${pct}%`;
    setStatus(`正在下载运行时 ${loaded} / ${total}`);
}

const nativeFetch = window.fetch.bind(window);
let frameworkStarted = 0;
let frameworkDone = 0;

window.fetch = async (input, init) => {
    const url = typeof input === "string"
        ? input
        : (input && input.url) ? input.url : String(input);

    try {
        const parsed = new URL(url, location.href);
        if (ART_HOSTS.includes(parsed.hostname) && (!init || !init.method || init.method === "GET")) {
            const cache = await caches.open(ART_CACHE);
            const cached = await cache.match(parsed.href);
            if (cached) return cached;
            const response = await nativeFetch(input, init);
            if (response.ok) {
                try { await cache.put(parsed.href, response.clone()); } catch { /* 配额满时忽略 */ }
            }
            return response;
        }

        if (parsed.pathname.includes("/_framework/")) {
            frameworkStarted += 1;
            try {
                const response = await nativeFetch(input, init);
                frameworkDone += 1;
                setProgress(frameworkDone, Math.max(frameworkStarted, frameworkDone));
                return response;
            } catch (err) {
                frameworkDone += 1;
                throw err;
            }
        }
    } catch {
        // 解析失败或 Cache API 不可用时走原 fetch
    }

    return nativeFetch(input, init);
};

let builder = dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery();

if (typeof builder.withModuleConfig === "function") {
    builder = builder.withModuleConfig({
        onDownloadResourceProgress: (loaded, total) => setProgress(loaded, total)
    });
} else {
    setStatus("正在下载运行时与中文字体…");
}

const dotnetRuntime = await builder.create();
setStatus("正在启动界面…");

const config = dotnetRuntime.getConfig();
await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
