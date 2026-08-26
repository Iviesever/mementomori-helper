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
<title>MementoMori 账号导出器</title>
<style>
:root { color-scheme: dark; font-family: system-ui,-apple-system,"Segoe UI","Microsoft YaHei",sans-serif; }
* { box-sizing: border-box; }
body { margin:0; background:#0f172a; color:#e5e7eb; }
main { max-width:980px; margin:28px auto; padding:0 18px 36px; }
.card { background:#1e293b; border:1px solid #334155; border-radius:15px; padding:20px; margin-bottom:14px; box-shadow:0 10px 28px rgba(0,0,0,.2); }
h1 { margin:0 0 8px; font-size:28px; }
h2 { margin:0 0 14px; font-size:19px; }
p { line-height:1.6; }
.sub { color:#94a3b8; margin:0; }
.good { color:#86efac; }
.warn { color:#fbbf24; }
.badge { display:inline-block; padding:3px 8px; margin-left:8px; border-radius:999px; font-size:12px; background:#064e3b; color:#a7f3d0; vertical-align:middle; }
.toolbar,.actions,.row { display:flex; flex-wrap:wrap; gap:9px; align-items:center; }
.toolbar { margin-bottom:14px; }
button { border:0; border-radius:9px; padding:10px 14px; font-weight:650; cursor:pointer; }
button.primary { background:#2563eb; color:#fff; padding:12px 20px; }
button.secondary { background:#334155; color:#f1f5f9; }
button.danger { background:#7f1d1d; color:#fee2e2; }
button:disabled { opacity:.55; cursor:wait; }
.grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(220px,1fr)); gap:9px; }
.option,.format-option { display:flex; gap:10px; align-items:flex-start; padding:11px; border:1px solid #334155; border-radius:10px; background:#0f172a; }
.option strong,.format-option strong { display:block; margin-bottom:3px; }
.option small,.format-option small { display:block; color:#94a3b8; line-height:1.45; }
input[type=checkbox],input[type=radio] { width:18px; height:18px; margin-top:2px; flex:0 0 auto; }
.formats { display:grid; gap:9px; }
.statusbox { margin-top:14px; padding:12px 14px; min-height:48px; border-radius:10px; background:#0f172a; border:1px solid #334155; white-space:pre-wrap; color:#93c5fd; }
.counter { margin-left:auto; color:#94a3b8; font-size:13px; }
code { background:#0f172a; padding:2px 5px; border-radius:5px; }
@media(max-width:600px){ main{margin-top:14px} h1{font-size:23px}.counter{width:100%;margin-left:0} }
</style>
</head>
<body>
<main>
<section class="card">
    <h1>MementoMori 账号导出器 <span class="badge">本机访问</span></h1>
    <p class="sub">V3.2：启动一次后可以连续导出，不需要每导一次就重新登录、重新等待。导出接口仅允许 <code>127.0.0.1</code> 回环访问。</p>
</section>

<section class="card">
    <h2>1. 选择内容</h2>
    <div class="toolbar">
        <button type="button" class="secondary" onclick="preset('quest')">推图分析</button>
        <button type="button" class="secondary" onclick="preset('character')">角色养成</button>
        <button type="button" class="secondary" onclick="preset('light')">轻量账号</button>
        <button type="button" class="secondary" onclick="preset('gacha')">抽卡分析</button>
        <button type="button" class="secondary" onclick="preset('all')">全部</button>
        <button type="button" class="secondary" onclick="preset('none')">清空</button>
    </div>
    <div class="grid">
        <label class="option"><input type="checkbox" name="section" value="player" checked><span><strong>玩家信息</strong><small>Rank、经验、VIP 等</small></span></label>
        <label class="option"><input type="checkbox" name="section" value="progress" checked><span><strong>主线进度</strong><small>已通过关卡、下一关</small></span></label>
        <label class="option"><input type="checkbox" name="section" value="levelLink" checked><span><strong>Level Link</strong><small>等级、槽位、成员</small></span></label>
        <label class="option"><input type="checkbox" name="section" value="characters" checked><span><strong>角色</strong><small>稀有度、等级、战力、战斗参数</small></span></label>
        <label class="option"><input type="checkbox" name="section" value="equipment" checked><span><strong>装备 / 符石</strong><small>强化、研磨、圣装/魔装、符石</small></span></label>
        <label class="option"><input type="checkbox" name="section" value="decks" checked><span><strong>编队</strong><small>Boss、塔、Raid 等保存阵容</small></span></label>
        <label class="option"><input type="checkbox" name="section" value="items" checked><span><strong>物品 / 资源</strong><small>钻石、金币、培养材料、票券</small></span></label>
        <label class="option"><input type="checkbox" name="section" value="gacha" checked><span><strong>卡池 / 保底</strong><small>只有勾选时才读取 Gacha/GetList</small></span></label>
    </div>
</section>

<section class="card">
    <h2>2. 文件形式</h2>
    <div class="formats">
        <label class="format-option"><input type="radio" name="exportMode" value="compact" checked><span><strong>紧凑 JSON（推荐）</strong><small>最适合直接发给 ChatGPT，文件最小。</small></span></label>
        <label class="format-option"><input type="radio" name="exportMode" value="pretty"><span><strong>格式化 JSON</strong><small>带缩进和换行，方便自己查看。</small></span></label>
        <label class="format-option"><input type="radio" name="exportMode" value="zip"><span><strong>ZIP 分模块</strong><small>适合归档和拆分查看。</small></span></label>
    </div>
</section>

<section class="card">
    <h2>3. 导出</h2>
    <p class="good"><strong>默认保持导出器运行。</strong>下载完成后可以立刻换预设、换模块、再次导出，不需要重新启动。</p>
    <label class="row">
        <input id="autoClose" type="checkbox">
        <span>这次导出完成后自动关闭导出器（通常不要勾）</span>
    </label>
    <p class="warn">这是非官方 helper。此页面减少误操作和不必要的数据读取，但不代表整个 helper 是只读客户端。</p>
    <div class="actions">
        <button id="exportBtn" type="button" class="primary" onclick="exportData()">导出所选数据</button>
        <button id="closeBtn" type="button" class="danger" onclick="shutdownServer()">用完后关闭导出器</button>
        <span id="counter" class="counter">本次会话已导出 0 次</span>
    </div>
    <div id="status" class="statusbox">已就绪。选择内容后点击“导出所选数据”。</div>
</section>
</main>
<script>
const sectionBoxes = () => Array.from(document.querySelectorAll('input[name="section"]'));
let exportCount = 0;

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
    const p = n => String(n).padStart(2,'0');
    return `${d.getFullYear()}${p(d.getMonth()+1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
}

function setStatus(message, error = false) {
    const el = document.getElementById('status');
    el.textContent = message;
    el.style.color = error ? '#fca5a5' : '#93c5fd';
}

async function shutdownServer() {
    const closeBtn = document.getElementById('closeBtn');
    const exportBtn = document.getElementById('exportBtn');
    closeBtn.disabled = true;
    exportBtn.disabled = true;
    setStatus('正在关闭导出器并清理临时登录配置…');
    try {
        await fetch('/safe-export-ui/shutdown', { method:'POST' });
        setStatus('导出器已关闭，可以直接关闭本页面。');
    } catch (_) {
        setStatus('服务器连接已关闭，可以直接关闭本页面。');
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
        setStatus(`正在读取并导出：${sections.join(', ')}…\n首次启动时账号初始化会稍久；同一会话后续导出无需重新启动。`);
        const url = `/safe-export?sections=${encodeURIComponent(sections.join(','))}&format=${encodeURIComponent(format)}&style=${encodeURIComponent(style)}`;
        const response = await fetch(url, { method:'GET' });
        if (!response.ok) {
            const text = await response.text();
            throw new Error(`HTTP ${response.status}: ${text}`);
        }

        const blob = await response.blob();
        const objectUrl = URL.createObjectURL(blob);
        const fileName = `mementomori-account-${timestamp()}.${format === 'zip' ? 'zip' : 'json'}`;
        const anchor = document.createElement('a');
        anchor.href = objectUrl;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(objectUrl);

        exportCount += 1;
        document.getElementById('counter').textContent = `本次会话已导出 ${exportCount} 次`;
        const sizeKb = (blob.size / 1024).toFixed(1);

        if (document.getElementById('autoClose').checked) {
            setStatus(`导出完成：${fileName}\n大小：${sizeKb} KB\n正在自动关闭导出器…`);
            setTimeout(shutdownServer, 700);
        } else {
            setStatus(`导出完成：${fileName}\n大小：${sizeKb} KB\n可以继续修改选项并再次导出，无需重新启动。`);
            exportBtn.disabled = false;
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
