// candle-chart.js：K 線＋多個附圖的共用繪圖程式 (型態掃描小卡、技術圖表頁都用這裡)
// 由頁面的 .razor.js 以 import 載入；附圖定義、均線、配色都只在這裡維護一份

export const UP = '#ef5350';     // 漲：紅 (台股配色)
export const DOWN = '#26a69a';   // 跌：綠
export const AXIS_COLOR = '#8392A5';
const LINE_A = '#f5c242';         // 附圖第一條線
const LINE_B = '#42a5f5';         // 附圖第二條線

// 均線：欄位名 (對應 C# PatternCandle 的 camelCase)、圖例名稱、顏色。點圖例可隱藏 / 顯示該條線
// 顏色避開漲紅跌綠，以免和 K 棒混淆
export const MA_LINES = [
    { key: 'ma5', name: 'MA5', color: '#f5c242' },
    { key: 'ma10', name: 'MA10', color: '#ab47bc' },
    { key: 'ma20', name: 'MA20', color: '#42a5f5' },
    { key: 'ma60', name: 'MA60', color: '#ff9800' },
    { key: 'ma120', name: 'MA120', color: '#bdbdbd' }
];

// 布林通道：上下軌之間只填淡底色 (不畫線)；中軌就是 MA20，不重畫
const BOLL_FILL = 'rgba(131, 146, 165, 0.18)';

const HOLDING_COLOR = '#ab47bc';  // 外資持股比例線
const OIL_COLOR = '#ff9800';      // 原油線

// 費半列的價格 (已在 C# 取到小數 2 位)；缺值顯示 -
const px = v => v == null ? '-' : v;
const soxText = c => c.soxClose == null ? '費半 -'
    : `費半 開 ${px(c.soxOpen)} 高 ${px(c.soxHigh)} 低 ${px(c.soxLow)} 收 ${px(c.soxClose)}（美股 ${c.soxDate}）`;
// 費半均線的欄位名：MA_LINES 的 key 'ma5' → 'soxMa5' (C# PatternCandle.SoxMa5)
const soxMaKey = m => 'sox' + m.key[0].toUpperCase() + m.key.slice(1);

// 股 → 張 (千股)，四捨五入
const toLots = v => v == null ? null : Math.round(v / 1000);
const signed = v => v == null ? '-' : (v > 0 ? '+' : '') + v.toLocaleString();

// 三大法人類附圖：買賣超柱狀 (張，正紅負綠)，柱狀也列在圖例
function netChart(key, label, extraLines, extraTooltip) {
    return {
        title: label,
        bars: c => ({ value: toLots(c[key]), itemStyle: { color: (c[key] ?? 0) >= 0 ? UP : DOWN } }),
        barName: `${label}買賣超(張)`,
        lines: () => extraLines || [],
        hasData: c => c[key] != null,
        barText: c => `${label} ${signed(toLots(c[key]))} 張`,
        infoBar: c => `${signed(toLots(c[key]))} 張`,
        // 資訊列的買賣超數字依正負上色 (漲紅跌綠；0 或沒有資料維持一般顏色)
        infoColor: c => (c[key] ?? 0) > 0 ? UP : (c[key] ?? 0) < 0 ? DOWN : null,
        tooltip: c => `${label} ${signed(toLots(c[key]))} 張${extraTooltip ? extraTooltip(c) : ''}`
    };
}

// 附圖定義：bars = 柱狀 (可無)、barName = 柱狀的圖例名稱 (有才列入圖例)、lines = 線 (依指標參數命名；
// ownAxis = 用自己的 Y 軸，例如持股比例 % 與買賣超張數單位不同)、levels = 0~100 擺盪指標的參考線、
// hasData = 判斷該 K 棒有無資料 (整張小卡都沒有時顯示提示)
// settings 為 C# IndicatorSettings (camelCase)
export const SUB_CHARTS = {
    volume: {
        title: '成交量',
        infoBar: c => c.volume.toLocaleString(),
        bars: c => ({ value: c.volume, itemStyle: { color: c.close >= c.open ? UP : DOWN } }),
        lines: () => [{ key: 'volMa5', name: '量MA5', color: LINE_A, fmt: v => v.toLocaleString() },
                      { key: 'volMa10', name: '量MA10', color: '#ab47bc', fmt: v => v.toLocaleString() }],
        barText: c => `量 ${c.volume.toLocaleString()}`,
        tooltip: c => `量 ${c.volume.toLocaleString()}　量MA5 ${c.volMa5?.toLocaleString() ?? '-'}　量MA10 ${c.volMa10?.toLocaleString() ?? '-'}`
    },
    macd: {
        title: 'MACD',
        // OSC 柱狀：正紅負綠
        bars: c => ({ value: c.osc, itemStyle: { color: (c.osc ?? 0) >= 0 ? UP : DOWN } }),
        lines: () => [{ key: 'dif', name: 'DIF', color: LINE_A }, { key: 'macd', name: 'MACD', color: LINE_B }],
        barText: c => `OSC ${c.osc ?? '-'}`,
        infoColor: c => (c.osc ?? 0) > 0 ? UP : (c.osc ?? 0) < 0 ? DOWN : null,   // 資訊列的 OSC 數字依正負上色 (同柱狀；0 或沒有資料維持一般顏色)
        tooltip: c => `DIF ${c.dif ?? '-'}　MACD ${c.macd ?? '-'}　OSC ${c.osc ?? '-'}`
    },
    kd: {
        title: 'KD',
        lines: () => [{ key: 'k', name: 'K', color: LINE_A }, { key: 'd', name: 'D', color: LINE_B }],
        levels: [80, 20],
        tooltip: c => `K ${c.k ?? '-'}　D ${c.d ?? '-'}`
    },
    rsi: {
        title: 'RSI',
        lines: s => [{ key: 'rsiFast', name: `RSI${s.rsiFast}`, color: LINE_A }, { key: 'rsiSlow', name: `RSI${s.rsiSlow}`, color: LINE_B }],
        levels: [70, 30],
        tooltip: (c, s) => `RSI${s.rsiFast} ${c.rsiFast ?? '-'}　RSI${s.rsiSlow} ${c.rsiSlow ?? '-'}`
    },
    // 三大法人：只畫合計買賣超；外資另畫持股比例；投信、自營商只畫買賣超 (使用者指定)
    inst: netChart('totalNet', '三大法人'),
    foreign: netChart('foreignNet', '外資',
        [{ key: 'foreignHoldingPct', name: '持股%', label: '持股', color: HOLDING_COLOR, ownAxis: true, fmt: v => v + '%' }],
        c => `　持股 ${c.foreignHoldingPct != null ? c.foreignHoldingPct + '%' : '-'}`),
    trust: netChart('trustNet', '投信'),
    dealer: netChart('dealerNet', '自營商'),
    // 原油：Brent 當日均價 (美元 / 桶；取當天之前最近一筆，見 C# PatternScanService.ApplyOil)，一條線、自己的 Y 軸
    oil: {
        title: '原油',
        lines: () => [{ key: 'oil', name: 'Brent', label: 'Brent 原油', color: OIL_COLOR, ownAxis: true, fmt: v => '$' + v }],
        hasData: c => c.oil != null,
        noDataText: '尚無原油資料（請到「資料更新」頁同步）',
        tooltip: c => `Brent 原油 ${c.oil != null ? '$' + c.oil : '-'}`
    },
    // 費半 (美國費城半導體指數)：完整紅綠蠟燭，自己的 Y 軸。日期已在 C# 對齊到台股交易日
    // (PatternScanService.ApplyMarketIndex：取該日之前已收盤的美股，台股連假期間的多個美股交易日合併成一根；美股休市日留空)。
    // heightScale：蠟燭要看得清楚，這一列比其他附圖高 (使用者指定 2 倍)
    sox: {
        title: '費半',
        heightScale: 2,
        candleName: '費半',
        candle: c => c.soxClose == null ? '-' : [c.soxOpen, c.soxClose, c.soxLow, c.soxHigh],
        lines: () => [],
        hasData: c => c.soxClose != null,
        noDataText: '尚無費半資料（請到「資料更新」頁同步）',
        barText: soxText,
        infoBar: c => soxText(c).replace(/^費半 /, ''),
        // 費半也畫均線：和個股共用「顯示均線」的勾選 (同名的 series 由同一個圖例控制，見 buildStackedOption)
        ma: true,
        tooltip: c => `${soxText(c)}　${MA_LINES.map(m => `${m.name} ${c[soxMaKey(m)] ?? '-'}`).join('　')}`
    }
};

// 某個附圖列的繪圖區高度 (預設 layout.subHeight；定義了 heightScale 的附圖依倍數加高)
export const subHeightOf = (sub, layout) => layout.subHeight * (SUB_CHARTS[sub]?.heightScale ?? 1);

// 附圖一列的高度 = 標題區 (subLegend) + 繪圖區 + 下方內距 (layout.subPad，技術圖表為 3；小卡沒有)
export const subRowHeightOf = (sub, layout) => layout.subLegend + subHeightOf(sub, layout) + (layout.subPad ?? 0);

// 每個附圖列的位置：row = 這一列的範圍 (含上方間隔)，band = 淡底色帶 (不含上方間隔)。
// K 線區底部那條軸線本身佔 1px，第一條色帶多讓 1px，視覺上與 K 線圖才剛好隔 subGap (只有指定 subGap 的技術圖表)
export function subRows(layout, subs) {
    const rows = [];
    let top = layout.top + layout.candleHeight;
    subs.forEach((sub, index) => {
        const height = subRowHeightOf(sub, layout);
        const bandTop = top + (layout.subGap ?? SUB_BG_GAP) + (index === 0 && layout.subGap != null ? 1 : 0);
        rows.push({ sub, top, height, bandTop, bandBottom: top + height });
        top += height;
    });
    return rows;
}

// 各附圖列的淡底色：用容器的 CSS 背景 (線性漸層的硬邊界) 畫在圖表後面，圖表本身是透明的，
// 所以寬度隨容器、不必隨視窗重算。每列頂端留 layout.subGap (預設 SUB_BG_GAP；技術圖表為 6) 不上色，
// 相鄰兩列之間、K 線區與第一個附圖之間才看得出區隔。
// 用半透明色，技術圖表 (#1e222d) 與小卡 (深色卡片) 的底色都適用。
export const SUB_BG = 'rgba(131, 146, 165, 0.09)';
const SUB_BG_GAP = 3;
export function stackedBackground(layout, subs) {
    if (subs.length === 0) return 'none';
    const stops = [];
    for (const { bandTop: from, bandBottom: bottom } of subRows(layout, subs)) {
        stops.push(`transparent ${from}px`, `${SUB_BG} ${from}px`, `${SUB_BG} ${bottom}px`, `transparent ${bottom}px`);
    }
    return `linear-gradient(to bottom, transparent 0px, ${stops.join(', ')}, transparent 100%)`;
}

// 整張圖的高度：K 線區 + 每個附圖列 (圖例列 + 繪圖區) + 上下留白；頁面設定容器高度用
export const stackedHeight = (layout, subs) =>
    layout.top + layout.candleHeight + subs.reduce((sum, sub) => sum + subRowHeightOf(sub, layout), 0) + layout.bottom;
// 附圖 Y 軸刻度 (圖小，只留約 3 個刻度)：萬、億縮寫，避免數字太長
const compact = v => {
    const a = Math.abs(v);
    if (a >= 1e8) return +(v / 1e8).toFixed(1) + '億';
    if (a >= 1e4) return +(v / 1e4).toFixed(1) + '萬';
    return +v.toFixed(2);
};
const AXIS_SPLIT = { lineStyle: { color: '#2d3139' } };
const NO_DATA_TEXT = '尚無三大法人資料（請到「資料更新」頁同步）';

export function legendStyle(extra) {
    return {
        itemWidth: 12, itemHeight: 7, itemGap: 8, textStyle: { color: AXIS_COLOR, fontSize: 12 },
        // 隱藏中的線在圖例上調暗 (預設淺灰色會和 MA120 的灰色混淆)
        inactiveColor: '#3a3f4b', inactiveBorderColor: '#3a3f4b',
        ...extra
    };
}


// 資訊列 (滑鼠所在那一天的數值，顯示在各圖上方；headerInfo 模式用，取代提示框與圖例)
// 每個欄位 (日期、開、高、低、收、各均線、附圖標題與數值…) 都是固定寬度：寬度取「整份資料裡該欄位最長的文字」，
// 所以滑鼠移動時「開 2310」與「開 2310.13」佔一樣寬，後面的欄位不會跟著左右跳動。
// 文字用 ECharts rich text：{樣式key|文字}，樣式 (顏色、粗體、寬度) 依需要登記在 RICH
const RICH = {};
const INFO_FONT = '13px sans-serif';   // 與圖上資訊文字 (fontSize 13，ECharts 預設字體) 一致，量文字寬度用
const INFO_GAP = 14;                   // 欄位之間的間隔 (含在欄位寬度內)
const NEUTRAL = '#d1d4dc';
let measureCtx;
const textWidth = (text, bold) => {
    measureCtx ??= document.createElement('canvas').getContext('2d');
    measureCtx.font = (bold ? 'bold ' : '') + INFO_FONT;
    return measureCtx.measureText(text).width;
};
const val = (v, fmt) => v == null ? '-' : (fmt ? fmt(v) : v);

// 某一天的所有欄位：{ key = 欄位識別 (同一欄位在不同天的 key 相同，用來算最大寬度), text, color, bold }
// scope：'main' = K 線上方；附圖代號 = 該附圖上方。附圖標題固定共用 'title'，各附圖的數值才對得齊
function fieldsOf(scope, c, settings, visibleMas) {
    const shown = MA_LINES.filter(m => (visibleMas || []).includes(m.name));
    if (scope === 'main') {
        const color = c.close >= c.open ? UP : DOWN;
        return [
            { key: 'main|date', text: c.date, color: '#fff', bold: true },
            { key: 'main|o', text: `開 ${c.open}`, color }, { key: 'main|h', text: `高 ${c.high}`, color },
            { key: 'main|l', text: `低 ${c.low}`, color }, { key: 'main|c', text: `收 ${c.close}`, color },
            ...shown.map(m => ({ key: 'main|' + m.key, text: `${m.name} ${val(c[m.key])}`, color: m.color })),
            ...(settings.bollinger ? [{ key: 'main|boll', text: `布林 上 ${val(c.bollUpper)} 下 ${val(c.bollLower)}`, color: '#8392a5' }] : [])
        ];
    }
    const def = SUB_CHARTS[scope];
    const fields = [];
    if (def.title) fields.push({ key: 'title', text: def.title, color: '#fff', bold: true });   // 附圖標題 (粗體)
    const bar = def.infoBar ?? def.barText;
    if (bar) fields.push({ key: scope + '|bar', text: bar(c), color: def.infoColor?.(c) ?? NEUTRAL });
    for (const l of def.lines(settings)) fields.push({ key: scope + '|' + l.key, text: `${l.label ?? l.name} ${val(c[l.key], l.fmt)}`, color: l.color });
    // 有均線的附圖 (費半)：只列「顯示均線」已勾選的，與 K 線上方的資訊列一致
    if (def.ma) for (const m of shown) fields.push({ key: scope + '|' + m.key, text: `${m.name} ${val(c[soxMaKey(m)])}`, color: m.color });
    return fields;
}

// 各欄位的寬度 (像素)：掃過所有 K 棒，取每個欄位最長文字的寬度 (+ 間隔)。資料、均線勾選、指標參數改變時重算
export function infoWidths(candles, subs, settings, visibleMas) {
    const widths = {};
    for (const c of candles) {
        for (const scope of ['main', ...subs]) {
            for (const f of fieldsOf(scope, c, settings, visibleMas)) {
                const w = textWidth(f.text, f.bold);
                if (w > (widths[f.key] ?? 0)) widths[f.key] = w;
            }
        }
    }
    for (const key of Object.keys(widths)) widths[key] = Math.ceil(widths[key]) + INFO_GAP;
    return widths;
}

// 回傳 { id: 文字 }：'info-main' = K 線上方；'info-<附圖代號>' = 各附圖上方。widths 來自 infoWidths
export function infoTexts(candles, subs, settings, visibleMas, index, widths) {
    const c = candles[index];
    if (!c) return {};
    const render = fields => fields.map(f => {
        const width = widths?.[f.key];
        const style = `r${f.color.replace('#', '')}${f.bold ? 'b' : ''}${width ?? ''}`;
        RICH[style] ??= { fill: f.color, ...(f.bold ? { fontWeight: 'bold' } : {}), ...(width ? { width, align: 'left' } : {}) };
        return `{${style}|${f.text}}`;
    }).join('');
    const texts = { 'info-main': render(fieldsOf('main', c, settings, visibleMas)) };
    for (const sub of subs) texts['info-' + sub] = render(fieldsOf(sub, c, settings, visibleMas));
    return texts;
}
export const infoRich = () => RICH;

// K 線區 (高度固定) + 附圖依序疊在下方，所有 grid 共用同一條時間軸
// layout：{ top, candleHeight, subLegend, subHeight, left, right }；mainSeries：K 線 series (可帶頁面自己的標記)
// tooltipTitle(c)：提示框標題；dateFormatter：最下方日期標籤格式；maLegend：均線圖例的位置
// subAxes：true 時每個附圖都顯示 Y 軸刻度 (技術圖表頁用；型態掃描小卡空間小，不顯示)
export function buildStackedOption({ candles, subs, settings, layout, mainSeries, tooltipTitle, dateFormatter, maLegend, headerInfo, subAxes }) {
    const dates = candles.map(c => c.date);
    const LAYOUT = layout;
    const axisLine = { lineStyle: { color: AXIS_COLOR } };
    const lastGrid = subs.length;   // 最下方的 grid 才顯示日期

    const grids = [{ left: LAYOUT.left, right: LAYOUT.right, top: LAYOUT.top, height: LAYOUT.candleHeight }];
    const xAxes = [{ type: 'category', data: dates, axisLine, axisTick: { show: false },
                     axisLabel: { show: lastGrid === 0, color: AXIS_COLOR, fontSize: 10, formatter: dateFormatter } }];
    const yAxes = [{ scale: true, axisLine, axisLabel: { color: AXIS_COLOR, fontSize: 10 }, splitLine: { lineStyle: { color: '#2d3139' } } }];
    const legends = [legendStyle({ data: MA_LINES.map(m => m.name), ...maLegend })];
    const series = [mainSeries];
    const extraYAxes = [];   // 附圖自己的第二個 Y 軸，排在每個 grid 的主 Y 軸之後 (索引才不會錯位)
    const graphics = [];     // 附圖無資料時的提示文字

    for (const m of MA_LINES) {
        series.push({ name: m.name, type: 'line', data: candles.map(c => c[m.key]), showSymbol: false,
                      lineStyle: { width: 1, color: m.color }, itemStyle: { color: m.color } });
    }

    if (settings.bollinger) {
        // 堆疊面積：下軌透明打底，(上軌 − 下軌) 疊上去填色；z 低於 K 線與均線
        const band = { type: 'line', stack: 'boll', showSymbol: false, silent: true, z: 1, lineStyle: { opacity: 0 }, tooltip: { show: false } };
        series.push({ ...band, name: 'BOLL下軌', data: candles.map(c => c.bollLower) });
        series.push({ ...band, name: 'BOLL帶寬', data: candles.map(c => c.bollUpper == null || c.bollLower == null ? null : +(c.bollUpper - c.bollLower).toFixed(4)),
                      areaStyle: { color: BOLL_FILL } });
    }

    let usedHeight = 0;   // 已排掉的附圖列高度 (各列高度可不同，所以逐列累加)
    subs.forEach((sub, i) => {
        const def = SUB_CHARTS[sub];
        const g = i + 1;
        const subHeight = subHeightOf(sub, LAYOUT);
        const lineDefs = def.lines(settings);
        // 有柱狀或「用主 Y 軸的線」才有主 Y 軸內容；只有自己 Y 軸的線 (原油) 就把那條軸放在左側當主軸
        const hasPrimary = !!def.bars || lineDefs.some(l => !l.ownAxis);
        const top = LAYOUT.top + LAYOUT.candleHeight + usedHeight + LAYOUT.subLegend;
        usedHeight += LAYOUT.subLegend + subHeight + (LAYOUT.subPad ?? 0);
        grids.push({ left: LAYOUT.left, right: LAYOUT.right, top, height: subHeight });
        xAxes.push({ type: 'category', gridIndex: g, data: dates, axisLine, axisTick: { show: false },
                     axisLabel: { show: g === lastGrid, color: AXIS_COLOR, fontSize: 10, formatter: dateFormatter } });
        // KD、RSI 固定 0~100，參考線位置才有意義
        yAxes.push({ gridIndex: g, axisLabel: { show: false }, axisLine: { show: false }, splitLine: { show: false },
                     ...(def.levels ? { min: 0, max: 100 } : {}),
                     // 蠟燭附圖：Y 軸貼合價格範圍並顯示價格刻度
                     ...(def.candle ? { scale: true, axisLabel: { show: true, color: AXIS_COLOR, fontSize: 9 }, splitLine: { show: true, lineStyle: { color: '#2d3139' } } } : {}),
                     // 技術圖表頁：柱狀 / 線的附圖在左側顯示約 3 個刻度 (KD、RSI 固定 0 / 50 / 100)
                     ...(subAxes && !def.candle && hasPrimary ? {
                         axisLabel: { show: true, color: AXIS_COLOR, fontSize: 9, formatter: compact },
                         splitLine: { show: true, ...AXIS_SPLIT }, splitNumber: 1, ...(def.levels ? { interval: 50 } : {}) } : {}) });

        if (def.candle) {
            series.push({ name: def.candleName, type: 'candlestick', xAxisIndex: g, yAxisIndex: g, data: candles.map(def.candle),
                          itemStyle: { color: UP, color0: DOWN, borderColor: UP, borderColor0: DOWN } });
        }
        if (def.ma) {
            // 均線 series 與個股的均線同名 (MA5…)：ECharts 的圖例以名稱控制顯示，所以「顯示均線」勾選 (頁面設定
            // option.legend[0].selected) 會同時開關個股與費半的均線；不放進本附圖自己的圖例，避免重複
            for (const m of MA_LINES) {
                series.push({ name: m.name, type: 'line', xAxisIndex: g, yAxisIndex: g, data: candles.map(c => c[soxMaKey(m)]), showSymbol: false,
                              lineStyle: { width: 1, color: m.color }, itemStyle: { color: m.color } });
            }
        }

        if (def.bars) {
            series.push({ name: def.barName ?? (sub === 'macd' ? 'OSC' : undefined), type: 'bar', xAxisIndex: g, yAxisIndex: g,
                          data: candles.map(def.bars), ...(def.barName ? { itemStyle: { color: AXIS_COLOR } } : {}) });
        }
        const lines = lineDefs;
        lines.forEach((l, j) => {
            let yAxisIndex = g;
            if (l.ownAxis) {
                yAxisIndex = subs.length + 1 + extraYAxes.length;
                extraYAxes.push({ gridIndex: g, scale: true, axisLabel: { show: false }, axisLine: { show: false }, splitLine: { show: false },
                    // 技術圖表頁：自己的 Y 軸也顯示刻度 (有主軸時放右側、用線的顏色；單獨存在時放左側)
                    ...(subAxes ? {
                        position: hasPrimary ? 'right' : 'left', splitNumber: 1,
                        axisLabel: { show: true, color: hasPrimary ? l.color : AXIS_COLOR, fontSize: 9, formatter: l.fmt ?? compact },
                        ...(hasPrimary ? {} : { splitLine: { show: true, ...AXIS_SPLIT } }) } : {}) });
            }
            series.push({
                name: l.name, type: 'line', xAxisIndex: g, yAxisIndex, data: candles.map(c => c[l.key]), connectNulls: !!l.ownAxis,
                showSymbol: false, lineStyle: { width: 1, color: l.color }, itemStyle: { color: l.color },
                // 超買 / 超賣參考線 (掛在第一條線上即可)
                markLine: def.levels && j === 0 ? {
                    silent: true, symbol: 'none', label: { show: true, position: 'start', color: '#5d6270', fontSize: 9, distance: 2 },
                    lineStyle: { color: '#5d6270', type: 'dashed', width: 1 },
                    data: def.levels.map(v => ({ yAxis: v }))
                } : undefined
            });
        });
        // 附圖圖例：放在該附圖左上方
        legends.push(legendStyle({ data: [...(def.barName ? [def.barName] : []), ...(def.candleName ? [def.candleName] : []), ...lines.map(l => l.name)], top: top - LAYOUT.subLegend + (LAYOUT.subGap ?? 3), left: LAYOUT.left }));
        if (headerInfo) graphics.push({ id: 'info-' + sub, type: 'text', left: LAYOUT.left, top: top - LAYOUT.subLegend + (LAYOUT.subGap ?? 3) - 1 + (LAYOUT.subPad ?? 0), silent: true, style: { text: '', fontSize: 13, fill: '#d1d4dc' } });
        if (def.hasData && !candles.some(def.hasData)) {
            graphics.push({ type: 'text', left: 'center', top: top + subHeight / 2 - 6,
                            style: { text: def.noDataText ?? NO_DATA_TEXT, fill: '#5d6270', fontSize: 11 } });
        }
    });
    yAxes.push(...extraYAxes);

    if (headerInfo) {
        // 改在各圖上方顯示當天數值：不用提示框 (只留十字線)，圖例隱藏 (仍保留均線的顯示狀態)，日期只在 K 線上方出現一次
        graphics.push({ id: 'info-main', type: 'text', left: LAYOUT.left, top: maLegend.top, silent: true, style: { text: '', fontSize: 13, fill: '#d1d4dc' } });
        legends.forEach(l => { l.show = false; });
        xAxes.forEach(x => { x.axisPointer = { label: { show: false } }; });
    }

    return {
        animation: false,
        legend: legends,
        tooltip: {
            trigger: 'axis',
            showContent: !headerInfo,
            axisPointer: { type: 'cross' },
            backgroundColor: 'rgba(30, 34, 45, 0.9)',
            borderColor: '#434651',
            textStyle: { color: '#d1d4dc', fontSize: 12 },
            formatter: params => {
                const c = candles[params[0].dataIndex];
                const color = c.close >= c.open ? UP : DOWN;
                return `<div style="font-weight:bold;color:#fff;">${tooltipTitle(c)}</div>
                    <div style="color:${color}">開 ${c.open}　高 ${c.high}<br/>低 ${c.low}　收 ${c.close}</div>
                    ${subs.map(s => `<div>${SUB_CHARTS[s].tooltip(c, settings)}</div>`).join('')}
                    ${MA_LINES.map(m => `<div style="color:${m.color}">${m.name} ${c[m.key] ?? '-'}</div>`).join('')}
                    ${settings.bollinger ? `<div style="color:#8392a5">布林 上 ${c.bollUpper ?? '-'}　下 ${c.bollLower ?? '-'}</div>` : ''}`;
            }
        },
        axisPointer: { link: [{ xAxisIndex: 'all' }] },
        graphic: graphics,
        grid: grids,
        xAxis: xAxes,
        yAxis: yAxes,
        series
    };
}
