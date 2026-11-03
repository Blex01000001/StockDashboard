/*
 * UI 快照比對工具：擷取各頁各狀態「所有元素的 computed style」與截圖，比對修改前後是否一致。
 * 需要 Playwright (雲端沙盒已預裝；本機則先 `npm install playwright` 並 `npx playwright install chromium`)。
 *
 * 用法：
 *   node tools/ui-snapshot.js capture <輸出資料夾> [--url http://localhost:8888] [--echarts <echarts.min.js 路徑>]
 *   node tools/ui-snapshot.js diff <資料夾A> <資料夾B> [--include-vars] [--tolerance 2]
 *
 * 典型流程 (改 CSS / UI 前後各擷取一次)：
 *   python3 tools/make-test-db.py /tmp/ui.db           # 測試資料庫，appsettings.json 指向它
 *   (啟動網站) → capture /tmp/before → (修改、重新建置、重啟網站) → capture /tmp/after
 *   node tools/ui-snapshot.js diff /tmp/before /tmp/after   # 有差異時 exit code = 1
 *
 * 注意：
 * - 兩次擷取要用同一份資料庫、同一天 (資料更新頁的日期欄位預設為今天)。
 * - 連不到 ECharts CDN 時 (雲端沙盒)，用 --echarts 指定本機檔案，例如：
 *     curl -sSL https://registry.npmjs.org/echarts/-/echarts-5.4.3.tgz | tar xz -C /tmp package/dist/echarts.min.js
 *     → --echarts /tmp/package/dist/echarts.min.js
 * - diff 預設忽略 CSS 自訂變數 (--xxx) 的差異；要比對時加 --include-vars。
 * - 截圖比對預設容許每個色版差 2 以內：圓角邊緣的反鋸齒每次繪製會有 ±1 的雜訊。--tolerance 0 為完全一致。
 * - 新增頁面或互動狀態時，修改下方 STATES。
 */
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 ? args[i + 1] : fallback;
};

// ── 要擷取的畫面狀態：[名稱, 動作] ──
const STATES = [
  ['q_init', async p => { await go(p, 'stock-query'); }],
  ['q_alert', async p => { await p.fill('.dashboard-panel input', ''); await clickMain(p); }],
  ['q_result', async p => { await p.fill('.dashboard-panel input', '2330'); await clickMain(p); }],
  ['q_focus', async p => { await p.focus('.dashboard-panel input'); }],
  ['q_btnhover', async p => { await p.hover('.dashboard-panel button'); }],
  ['q_navhover', async p => { await p.hover('a[href="stock-chart"]'); }],
  ['q_collapsed', async p => { await p.click('.toggle-btn'); await p.waitForTimeout(500); }],
  ['c_init', async p => { await p.click('.toggle-btn'); await p.waitForTimeout(500); await go(p, 'stock-chart'); }],
  ['c_alert', async p => { await p.fill('.search-input-wrapper input', 'NOPE'); await clickMain(p); }],
  ['c_drawn', async p => { await p.fill('.search-input-wrapper input', '2330'); await clickMain(p, 1500); }],
  ['c_subs', async p => {
    for (const name of ['MACD', '外資']) await p.locator('.sub-toggle', { hasText: name }).locator('input').check();
    await p.check('.ma-toggle:has-text("120") input'); await p.waitForTimeout(1200);
  }],
  // 切到別頁再回來：代號、附圖設定與圖表都保留
  ['c_kept', async p => { await p.click('a[href="stock-query"]'); await p.waitForTimeout(800); await p.click('a[href="stock-chart"]'); await p.waitForTimeout(1500); }],
  ['m_init', async p => { await go(p, 'stock-compare'); }],
  ['m_alert', async p => { await p.fill('.dashboard-panel input', ''); await clickMain(p); }],
  ['m_drawn', async p => { await p.fill('.dashboard-panel input', '2330,0050'); await clickMain(p, 1500); }],
  ['m_focus', async p => { await p.focus('.dashboard-panel select'); }],
  ['u_init', async p => { await go(p, 'stock-update'); }],
  ['u_error', async p => { const c = p.locator('.card-box').nth(0); await c.locator('input').fill(''); await c.locator('button').click(); await p.waitForTimeout(600); }],
  ['u_finmind_error', async p => { const c = p.locator('.card-box').nth(3); await c.locator('input[type=text]').fill(''); await c.locator('button').click(); await p.waitForTimeout(600); }],
  ['p_init', async p => { await go(p, 'pattern-scan'); }],
  ['p_param', async p => { await p.locator('label.param:has-text("當天實體") input').fill('3'); await p.waitForTimeout(500); }],
  ['p_result', async p => { await p.locator('label.param:has-text("當天實體") input').fill('1'); await p.fill('.stock-list-input', '2330,0050'); await p.click('.scan-btn'); await p.waitForTimeout(2000); }],
  ['p_page2', async p => { const next = p.locator('.pager button').last(); if (await next.count()) { await next.click(); await p.waitForTimeout(1500); } }],
  // 用側邊欄切換 (不可用 page.goto：整頁重新載入會建立新連線，畫面狀態本來就會重置)
  ['p_kept', async p => { await p.click('a[href="stock-chart"]'); await p.waitForTimeout(1000); await p.click('a[href="pattern-scan"]'); await p.waitForTimeout(2000); }],
  ['p_star', async p => { await p.selectOption('.pattern-select', 'morning-star'); await p.waitForTimeout(800); }],
  ['p_soldiers', async p => { await p.selectOption('.pattern-select', 'three-white-soldiers'); await p.waitForTimeout(500); await p.click('.scan-btn'); await p.waitForTimeout(2000); }],
  ['p_crows', async p => { await p.selectOption('.pattern-select', 'three-black-crows'); await p.waitForTimeout(500); await p.click('.scan-btn'); await p.waitForTimeout(2000); }],
  ['p_horizon', async p => { await p.selectOption('.pattern-select', 'bullish-engulfing'); await p.fill('.extra-horizon-input', '60,120'); await p.locator('.extra-horizon-input').blur(); await p.click('.scan-btn'); await p.waitForTimeout(2000); }],
  ['p_ma', async p => { await p.uncheck('.ma-toggle:has-text("60") input'); await p.check('.ma-toggle:has-text("120") input'); await p.waitForTimeout(800); }],
  ['p_macd', async p => { await p.locator('.sub-toggle', { hasText: 'MACD' }).locator('input').check(); await p.waitForTimeout(1000); }],
  ['p_kd', async p => { await p.locator('.sub-toggle', { hasText: 'KD' }).locator('input').check(); await p.waitForTimeout(1000); }],
  ['p_rsi', async p => { await p.locator('.sub-toggle', { hasText: 'RSI' }).locator('input').check(); await p.waitForTimeout(1000); }],
  // 三大法人附圖：2330 有資料、0050 沒有 (顯示提示)；先關掉 MACD/KD/RSI 讓小卡不會太高
  ['p_inst', async p => {
    for (const name of ['MACD', 'KD', 'RSI']) await p.locator('.sub-toggle', { hasText: name }).locator('input').uncheck();
    for (const name of ['三大法人', '外資', '投信', '自營商']) await p.locator('.sub-toggle', { hasText: name }).locator('input').check();
    await p.waitForTimeout(1200);
  }],
  ['x_404', async p => { await go(p, 'not-exist-page'); }],
];

let baseUrl;
async function go(page, route) { await page.goto(`${baseUrl}/${route}`); await page.waitForTimeout(1500); }
async function clickMain(page, wait = 800) { await page.locator('.dashboard-panel button').first().click(); await page.waitForTimeout(wait); }

// 在瀏覽器內收集所有元素 (含 ::before / ::after / ::placeholder) 的 computed style。
// 元素以「標籤路徑」為鍵，不含 class，讓「只加 class 但外觀不變」的修改不會被誤判為差異。
function collectStyles() {
  const skip = new Set(['STYLE', 'SCRIPT', 'LINK', 'META', 'TITLE', 'HEAD', 'CANVAS']);
  const result = {};
  const read = (el, pseudo) => {
    const cs = getComputedStyle(el, pseudo || null);
    const o = {};
    for (let i = 0; i < cs.length; i++) o[cs[i]] = cs.getPropertyValue(cs[i]);
    return o;
  };
  (function walk(el, base) {
    const count = {};
    for (const child of el.children) {
      if (skip.has(child.tagName)) continue;
      count[child.tagName] = (count[child.tagName] || 0) + 1;
      const key = `${base}/${child.tagName}[${count[child.tagName]}]`;
      result[key] = { class: child.getAttribute('class') || '', style: read(child) };
      for (const pseudo of ['::before', '::after'].concat(['INPUT', 'TEXTAREA'].includes(child.tagName) ? ['::placeholder'] : [])) {
        result[key + pseudo] = { class: '', style: read(child, pseudo) };
      }
      if (!/chart-container/.test(child.id)) walk(child, key);   // 不深入 ECharts 自行產生的 DOM
    }
  })(document.documentElement, '');
  return result;
}

async function capture(outDir) {
  baseUrl = option('--url', 'http://localhost:8888');
  const echarts = option('--echarts', process.env.ECHARTS_JS);
  fs.mkdirSync(outDir, { recursive: true });

  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
  if (echarts) {
    await page.route(/echarts\.min\.js/, r => r.fulfill({ path: echarts, contentType: 'application/javascript' }));
  }
  const snapshot = {};
  for (const [name, action] of STATES) {
    await action(page);
    await page.waitForTimeout(400);
    snapshot[name] = await page.evaluate(collectStyles);
    await page.screenshot({ path: path.join(outDir, `${name}.png`) });
  }
  fs.writeFileSync(path.join(outDir, 'styles.json'), JSON.stringify(snapshot));
  await browser.close();
  console.log(`已擷取 ${STATES.length} 個狀態到 ${outDir}`);
}

// 在瀏覽器內用 canvas 逐像素比對兩張 PNG，回傳差異像素數與範圍
async function comparePngs(page, fileA, fileB, tolerance) {
  const toUrl = f => 'data:image/png;base64,' + fs.readFileSync(f).toString('base64');
  return page.evaluate(async ([a, b, tol]) => {
    const load = src => new Promise(res => { const img = new Image(); img.onload = () => res(img); img.src = src; });
    const [ia, ib] = await Promise.all([load(a), load(b)]);
    if (ia.width !== ib.width || ia.height !== ib.height) return { size: true };
    const pixels = img => {
      const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
      const ctx = c.getContext('2d'); ctx.drawImage(img, 0, 0);
      return ctx.getImageData(0, 0, img.width, img.height).data;
    };
    const da = pixels(ia), db = pixels(ib);
    let count = 0, minX = Infinity, minY = Infinity, maxX = -1, maxY = -1;
    for (let i = 0; i < da.length; i += 4) {
      if (Math.abs(da[i] - db[i]) > tol || Math.abs(da[i + 1] - db[i + 1]) > tol || Math.abs(da[i + 2] - db[i + 2]) > tol) {
        const x = (i / 4) % ia.width, y = Math.floor(i / 4 / ia.width);
        count++; minX = Math.min(minX, x); minY = Math.min(minY, y); maxX = Math.max(maxX, x); maxY = Math.max(maxY, y);
      }
    }
    return { count, box: count ? [minX, minY, maxX, maxY] : null };
  }, [toUrl(fileA), toUrl(fileB), tolerance]);
}

async function diff(dirA, dirB) {
  const includeVars = args.includes('--include-vars');
  const tolerance = Number(option('--tolerance', '2'));
  const a = JSON.parse(fs.readFileSync(path.join(dirA, 'styles.json')));
  const b = JSON.parse(fs.readFileSync(path.join(dirB, 'styles.json')));
  let total = 0;

  for (const state of new Set([...Object.keys(a), ...Object.keys(b)])) {
    const x = a[state] || {}, y = b[state] || {};
    const lines = [];
    for (const el of new Set([...Object.keys(x), ...Object.keys(y)])) {
      if (!x[el] || !y[el]) { lines.push(`  元素${x[el] ? '消失' : '新增'}: ${el}`); continue; }
      for (const prop of new Set([...Object.keys(x[el].style), ...Object.keys(y[el].style)])) {
        if (!includeVars && prop.startsWith('--')) continue;
        if (x[el].style[prop] !== y[el].style[prop]) {
          const cls = y[el].class ? ` (.${y[el].class.trim().split(/\s+/).filter(c => !c.startsWith('b-')).join('.')})` : '';
          lines.push(`  ${el}${cls} { ${prop}: ${x[el].style[prop]} → ${y[el].style[prop]} }`);
        }
      }
    }
    total += lines.length;
    console.log(`[${state}] 樣式差異 ${lines.length}`);
    lines.slice(0, 15).forEach(l => console.log(l));
    if (lines.length > 15) console.log(`  …另有 ${lines.length - 15} 筆`);
  }

  const browser = await chromium.launch();
  const page = await browser.newPage();
  let pixelDiffs = 0;
  for (const file of fs.readdirSync(dirA).filter(f => f.endsWith('.png')).sort()) {
    const other = path.join(dirB, file);
    if (!fs.existsSync(other)) { console.log(`${file}: B 中不存在`); pixelDiffs++; continue; }
    const r = await comparePngs(page, path.join(dirA, file), other, tolerance);
    if (r.size) { console.log(`${file}: 尺寸不同`); pixelDiffs++; }
    else if (r.count) { console.log(`${file}: ${r.count} 個像素不同，範圍 ${r.box.join(',')}`); pixelDiffs++; }
    else console.log(`${file}: 相同`);
  }
  await browser.close();

  console.log(`\n樣式差異共 ${total} 筆，截圖不同 ${pixelDiffs} 張`);
  process.exitCode = total || pixelDiffs ? 1 : 0;
}

(async () => {
  const [command, first, second] = args;
  if (command === 'capture' && first) await capture(first);
  else if (command === 'diff' && first && second) await diff(first, second);
  else { console.log(fs.readFileSync(__filename, 'utf8').split('*/')[0]); process.exitCode = 2; }
})();
