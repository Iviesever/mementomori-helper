namespace MementoMori.WebUI;

internal static class SafeExportUi
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/safe-export-ui", UiAsync);
        app.MapPost("/safe-export-ui/shutdown", ShutdownAsync);
    }

    private static IResult UiAsync(HttpContext context)
    {
        if (!SafeExport.IsLoopbackRequest(context))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return Results.Content(Html, "text/html; charset=utf-8");
    }

    private static IResult ShutdownAsync(HttpContext context, IHostApplicationLifetime lifetime)
    {
        if (!SafeExport.IsLoopbackRequest(context))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            lifetime.StopApplication();
        });

        return Results.Json(new { ok = true });
    }

    private const string Html = """
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>MementoMori 账号导出</title>
<style>
:root {
    color-scheme: dark;
    font-family: system-ui,-apple-system,"Segoe UI","Microsoft YaHei",sans-serif;
}
body {
    margin: 0;
    background: #111827;
    color: #e5e7eb;
}
main {
    max-width: 980px;
    margin: 40px auto;
    padding: 0 20px 40px;
}
.card {
    background: #1f2937;
    border: 1px solid #374151;
    border-radius: 16px;
    padding: 22px;
    margin-bottom: 16px;
    box-shadow: 0 12px 32px rgba(0,0,0,.22);
}
h1 {
    margin: 0 0 8px;
    font-size: 28px;
}
h2 {
    margin-top: 0;
}
.sub {
    margin: 0;
    color: #9ca3af;
    line-height: 1.65;
}
.badge {
    display: inline-block;
    padding: 3px 8px;
    border-radius: 999px;
    background: #064e3b;
    color: #a7f3d0;
    font-size: 12px;
    margin-left: 8px;
}
.toolbar {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    margin: 14px 0 18px;
}
button {
    border: 0;
    border-radius: 9px;
    padding: 10px 14px;
    cursor: pointer;
    font-weight: 600;
}
button.secondary {
    background: #374151;
    color: #f3f4f6;
}
button.primary {
    background: #2563eb;
    color: white;
    padding: 12px 20px;
}
button.danger {
    background: #7f1d1d;
    color: #fee2e2;
}
button:disabled {
    opacity: .55;
    cursor: wait;
}
.grid {
    display: grid;
    grid-template-columns: repeat(auto-fit,minmax(230px,1fr));
    gap: 10px;
}
.option {
    display: flex;
    gap: 10px;
    align-items: flex-start;
    padding: 12px;
    border: 1px solid #374151;
    border-radius: 10px;
    background: #111827;
}
.option strong {
    display: block;
    margin-bottom: 3px;
}
.option small {
    display: block;
    color: #9ca3af;
    line-height: 1.45;
}
input[type=checkbox], input[type=radio] {
    width: 18px;
    height: 18px;
    margin-top: 2px;
}
.row {
    display: flex;
    flex-wrap: wrap;
    gap: 18px;
    align-items: center;
}
.format-list {
    display: grid;
    gap: 10px;
}
.format-option {
    display: flex;
    gap: 10px;
    align-items: flex-start;
    padding: 12px;
    border: 1px solid #374151;
    border-radius: 10px;
    background: #111827;
}
.format-option strong {
    display: block;
    margin-bottom: 3px;
}
.format-option small {
    color: #9ca3af;
    line-height: 1.45;
}
.status {
    margin-top: 14px;
    min-height: 24px;
    color: #93c5fd;
    white-space: pre-wrap;
}
.note {
    color: #fbbf24;
    line-height: 1.55;
}
code {
    background: #111827;
    border-radius: 5px;
    padding: 2px 5px;
}
@media (max-width: 600px) {
    main { margin-top: 18px; }
    h1 { font-size: 24px; }
}
</style>
</head>
<body>
<main>
    <section class="card">
        <h1>MementoMori 账号导出 <span class="badge">Local only</span></h1>
        <p class="sub">
            V3.1：按需选择账号数据。页面与导出接口只允许本机回环访问。<br>
            未选择“卡池 / 保底”时，不会调用 <code>Gacha/GetList</code>。
        </p>
    </section>

    <section class="card">
        <h2>1. 选择导出内容</h2>
        <div class="toolbar">
            <button type="button" class="secondary" onclick="preset('all')">全部</button>
            <button type="button" class="secondary" onclick="preset('light')">轻量账号</button>
            <button type="button" class="secondary" onclick="preset('quest')">推图分析</button>
            <button type="button" class="secondary" onclick="preset('character')">角色养成</button>
            <button type="button" class="secondary" onclick="preset('gacha')">抽卡分析</button>
            <button type="button" class="secondary" onclick="preset('none')">清空</button>
        </div>

        <div class="grid">
            <label class="option">
                <input type="checkbox" name="section" value="player" checked>
                <span><strong>玩家信息</strong><small>名称、Rank、经验、VIP 等</small></span>
            </label>
            <label class="option">
                <input type="checkbox" name="section" value="progress" checked>
                <span><strong>主线进度</strong><small>已通过关卡、下一关、Boss 当日胜场</small></span>
            </label>
            <label class="option">
                <input type="checkbox" name="section" value="levelLink" checked>
                <span><strong>Level Link</strong><small>联结等级、槽位、成员与快照实例编号</small></span>
            </label>
            <label class="option">
                <input type="checkbox" name="section" value="characters" checked>
                <span><strong>角色</strong><small>等级、有效等级、稀有度、属性、战力与战斗参数</small></span>
            </label>
            <label class="option">
                <input type="checkbox" name="section" value="equipment" checked>
                <span><strong>装备 / 符石</strong><small>强化、附加参数、圣装/魔装、符石名称与等级</small></span>
            </label>
            <label class="option">
                <input type="checkbox" name="section" value="decks" checked>
                <span><strong>编队</strong><small>Boss、竞技场、塔、Raid 等已保存阵容</small></span>
            </label>
            <label class="option">
                <input type="checkbox" name="section" value="items" checked>
                <span><strong>物品 / 资源</strong><small>钻石、金币、培养材料、兑换币、票券等</small></span>
            </label>
            <label class="option">
                <input type="checkbox" name="section" value="gacha" checked>
                <span><strong>卡池 / 保底</strong><small>当前池、抽数、天井、奖励计数和消耗</small></span>
            </label>
        </div>
        <p class="sub" style="margin-top:14px">
            推荐平时用“轻量账号”；卡关时用“推图分析”；只有研究装备时才需要装备/符石明细。
        </p>
    </section>

    <section class="card">
        <h2>2. 文件形式</h2>
        <div class="format-list">
            <label class="format-option">
                <input type="radio" name="exportMode" value="compact" checked>
                <span><strong>紧凑 JSON（推荐）</strong><small>数据完整，不写缩进和多余换行；最适合直接发给 ChatGPT，文件最小。</small></span>
            </label>
            <label class="format-option">
                <input type="radio" name="exportMode" value="pretty">
                <span><strong>格式化 JSON</strong><small>内容与紧凑 JSON 相同，但带缩进和换行，适合自己查看。</small></span>
            </label>
            <label class="format-option">
                <input type="radio" name="exportMode" value="zip">
                <span><strong>ZIP 分模块文件</strong><small>生成 manifest.json，并按勾选模块拆成独立 JSON，适合归档。</small></span>
            </label>
        </div>
    </section>

    <section class="card">
        <h2>3. 导出</h2>
        <label class="row">
            <input id="autoClose" type="checkbox" checked>
            <span>下载完成后自动关闭临时导出服务器并清理临时登录配置</span>
        </label>
        <p class="note">
            这是非官方 helper。导出仍会走非官方登录 / 读取接口；本 UI 只是减少误操作和不必要的数据读取，并不代表整个 helper 是“只读客户端”。
        </p>
        <div class="row">
            <button id="exportBtn" type="button" class="primary" onclick="exportData()">导出所选数据</button>
            <button id="closeBtn" type="button" class="danger" onclick="shutdownServer()">关闭导出器</button>
        </div>
        <div id="status" class="status"></div>
    </section>
</main>
<script>
const sectionBoxes = () => Array.from(document.querySelectorAll('input[name="section"]'));

function preset(name) {
    const map = {
        all: ['player','progress','levelLink','characters','equipment','decks','items','gacha'],
        light: ['player','progress','levelLink','characters','decks','items'],
        quest: ['player','progress','levelLink','characters','equipment','decks','items'],
        character: ['player','levelLink','characters','equipment','items'],
        gacha: ['player','characters','items','gacha'],
        none: []
    };
    const selected = new Set(map[name] || []);
    sectionBoxes().forEach(box => box.checked = selected.has(box.value));
}

function timestamp() {
    const d = new Date();
    const p = n => String(n).padStart(2, '0');
    return `${d.getFullYear()}${p(d.getMonth()+1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
}

function setStatus(message, error = false) {
    const el = document.getElementById('status');
    el.textContent = message;
    el.style.color = error ? '#fca5a5' : '#93c5fd';
}

async function shutdownServer() {
    const closeBtn = document.getElementById('closeBtn');
    closeBtn.disabled = true;
    setStatus('正在关闭临时导出服务器…');
    try {
        await fetch('/safe-export-ui/shutdown', { method: 'POST' });
        setStatus('导出服务器已请求关闭，可以关闭本页面。');
    } catch (_) {
        setStatus('服务器连接已关闭，可以关闭本页面。');
    }
}

async function exportData() {
    const sections = sectionBoxes().filter(x => x.checked).map(x => x.value);
    if (sections.length === 0) {
        setStatus('至少选择一个导出模块。', true);
        return;
    }

    const mode = document.querySelector('input[name="exportMode"]:checked').value;
    const format = mode === 'zip' ? 'zip' : 'json';
    const style = mode === 'pretty' ? 'pretty' : 'compact';
    const exportBtn = document.getElementById('exportBtn');
    exportBtn.disabled = true;

    try {
        setStatus(`正在读取并导出：${sections.join(', ')}…`);
        const url = `/safe-export?sections=${encodeURIComponent(sections.join(','))}&format=${encodeURIComponent(format)}&style=${encodeURIComponent(style)}`;
        const response = await fetch(url, { method: 'GET' });

        if (!response.ok) {
            const text = await response.text();
            throw new Error(`HTTP ${response.status}: ${text}`);
        }

        const blob = await response.blob();
        const objectUrl = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = objectUrl;
        anchor.download = `mementomori-account-${timestamp()}.${format === 'zip' ? 'zip' : 'json'}`;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(objectUrl);

        const sizeKb = (blob.size / 1024).toFixed(1);
        setStatus(`导出完成：${anchor.download}\n大小：${sizeKb} KB\n模块：${sections.join(', ')}`);

        if (document.getElementById('autoClose').checked) {
            setTimeout(shutdownServer, 700);
        }
    } catch (error) {
        setStatus(`导出失败：${error.message || error}`, true);
        exportBtn.disabled = false;
    }
}
</script>
</body>
</html>
""";
}
