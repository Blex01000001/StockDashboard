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

// 股 → 張 (千股)，四捨五入
const toLots = v => v == null ? null : Math.round(v / 1000);
const signed = v => v == null ? '-' : (v > 0 ? '+' : '') + v.toLocaleString();

// 三大法人類附圖：買賣超柱狀 (張，正紅負綠)，柱狀也列在圖例
function netChart(key, label, extraLines, extraTooltip) {
    return {
        bars: c => ({ value: toLots(c[key]), itemStyle: { color: (c[key] ?? 0) >= 0 ? UP : DOWN } }),
        barName: `${label}買賣超(張)`,
        lines: () => extraLines || [],
        hasData: c => c[key] != null,
        barText: c => `${label} ${signed(toLots(c[key]))} 張`,
        tooltip: c => `${label} ${signed(toLots(c[key]))} 張${extraTooltip ? extraTooltip(c) : ''}`
    };
}

// 附圖定義：bars = 柱狀 (可無)、barName = 柱狀的圖例名稱 (有才列入圖例)、lines = 線 (依指標參數命名；
// ownAxis = 用自己的 Y 軸，例如持股比例 % 與買賣超張數單位不同)、levels = 0~100 擺盪指標的參考線、
// hasData = 判斷該 K 棒有無資料 (整張小卡都沒有時顯示提示)
// settings 為 C# IndicatorSettings (camelCase)
export const SUB_CHARTS = {
    volume: {
        bars: c => ({ value: c.volume, itemStyle: { color: c.close >= c.open ? UP : DOWN } }),
        lines: () => [{ key: 'volMa5', name: '量MA5', color: LINE_A, fmt: v => v.toLocaleString() },
                      { key: 'volMa10', name: '量MA10', color: '#ab47bc', fmt: v => v.toLocaleString() }],
        barText: c => `量 ${c.volume.toLocaleString()}`,
        tooltip: c => `量 ${c.volume.toLocaleString()}　量MA5 ${c.volMa5?.toLocaleString() ?? '-'}　量MA10 ${c.volMa10?.toLocaleString() ?? '-'}`
    },
    macd: {
        // OSC 柱狀：正紅負綠
        bars: c => ({ value: c.osc, itemStyle: { color: (c.osc ?? 0) >= 0 ? UP : DOWN } }),
        lines: () => [{ key: 'dif', name: 'DIF', color: LINE_A }, { key: 'macd', name: 'MACD', color: LINE_B }],
        barText: c => `OSC ${c.osc ?? '-'}`,
        tooltip: c => `DIF ${c.dif ?? '-'}　MACD ${c.macd ?? '-'}　OSC ${c.osc ?? '-'}`
    },
    kd: {
        lines: () => [{ key: 'k', name: 'K', color: LINE_A }, { key: 'd', name: 'D', color: LINE_B }],
        levels: [80, 20],
        tooltip: c => `K ${c.k ?? '-'}　D ${c.d ?? '-'}`
    },
    rsi: {
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
        lines: () => [{ key: 'oil', name: 'Brent', label: 'Brent 原油', color: OIL_COLOR, ownAxis: true, fmt: v => '$' + v }],
        hasData: c => c.oil != null,
        noDataText: '尚無原油資料（請到「資料更新」頁同步）',
        tooltip: c => `Brent 原油 ${c.oil != null ? '$' + c.oil : '-'}`
    }
};
const NO_DATA_TEXT = '尚無三大法人資料（請到「資料更新」頁同步）';

export function legendStyle(extra) {
    return {
        itemWidth: 12, itemHeight: 7, itemGap: 8, textStyle: { color: AXIS_COLOR, fontSize: 10 },
        // 隱藏中的線在圖例上調暗 (預設淺灰色會和 MA120 的灰色混淆)
        inactiveColor: '#3a3f4b', inactiveBorderColor: '#3a3f4b',
        ...extra
    };
}


// 資訊列 (滑鼠所在那一天的數值，顯示在各圖上方；headerInfo 模式用，取代提示框與圖例)
// 文字用 ECharts rich text：{色碼key|文字}，色碼 key 在 RICH 登記
const RICH = { w: { fill: '#fff', fontWeight: 'bold' }, n: { fill: '#d1d4dc' } };
const rc = (color, text) => {
    const key = 'c' + color.replace('#', '');
    RICH[key] = { fill: color };
    return `{${key}|${text}}`;
};
const val = (v, fmt) => v == null ? '-' : (fmt ? fmt(v) : v);

// 回傳 { id: 文字 }：'info-main' = K 線上方；'info-<附圖代號>' = 各附圖上方
export function infoTexts(candles, subs, settings, visibleMas, index) {
    const c = candles[index];
    if (!c) return {};
    const color = c.close >= c.open ? UP : DOWN;
    const main = [`{w|${c.date}}`,
        rc(color, `開 ${c.open}　高 ${c.high}　低 ${c.low}　收 ${c.close}`),
        ...MA_LINES.filter(m => (visibleMas || []).includes(m.name)).map(m => rc(m.color, `${m.name} ${val(c[m.key])}`)),
        ...(settings.bollinger ? [rc('#8392a5', `布林 上 ${val(c.bollUpper)} 下 ${val(c.bollLower)}`)] : [])].join('　');
    const texts = { 'info-main': main };
    for (const sub of subs) {
        const def = SUB_CHARTS[sub];
        const parts = [];
        if (def.barText) parts.push(`{n|${def.barText(c)}}`);
        for (const l of def.lines(settings)) parts.push(rc(l.color, `${l.label ?? l.name} ${val(c[l.key], l.fmt)}`));
        texts['info-' + sub] = parts.join('　');
    }
    return texts;
}
export const infoRich = () => RICH;

// K 線區 (高度固定) + 附圖依序疊在下方，所有 grid 共用同一條時間軸
// layout：{ top, candleHeight, subLegend, subHeight, left, right }；mainSeries：K 線 series (可帶頁面自己的標記)
// tooltipTitle(c)：提示框標題；dateFormatter：最下方日期標籤格式；maLegend：均線圖例的位置
export function buildStackedOption({ candles, subs, settings, layout, mainSeries, tooltipTitle, dateFormatter, maLegend, headerInfo }) {
    const dates = candles.map(c => c.date);
    const LAYOUT = layout;
    const SUB_ROW = layout.subLegend + layout.subHeight;
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

    subs.forEach((sub, i) => {
        const def = SUB_CHARTS[sub];
        const g = i + 1;
        const top = LAYOUT.top + LAYOUT.candleHeight + i * SUB_ROW + LAYOUT.subLegend;
        grids.push({ left: LAYOUT.left, right: LAYOUT.right, top, height: LAYOUT.subHeight });
        xAxes.push({ type: 'category', gridIndex: g, data: dates, axisLine, axisTick: { show: false },
                     axisLabel: { show: g === lastGrid, color: AXIS_COLOR, fontSize: 10, formatter: dateFormatter } });
        // KD、RSI 固定 0~100，參考線位置才有意義
        yAxes.push({ gridIndex: g, axisLabel: { show: false }, axisLine: { show: false }, splitLine: { show: false },
                     ...(def.levels ? { min: 0, max: 100 } : {}) });

        if (def.bars) {
            series.push({ name: def.barName ?? (sub === 'macd' ? 'OSC' : undefined), type: 'bar', xAxisIndex: g, yAxisIndex: g,
                          data: candles.map(def.bars), ...(def.barName ? { itemStyle: { color: AXIS_COLOR } } : {}) });
        }
        const lines = def.lines(settings);
        lines.forEach((l, j) => {
            let yAxisIndex = g;
            if (l.ownAxis) {
                yAxisIndex = subs.length + 1 + extraYAxes.length;
                extraYAxes.push({ gridIndex: g, scale: true, axisLabel: { show: false }, axisLine: { show: false }, splitLine: { show: false } });
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
        legends.push(legendStyle({ data: [...(def.barName ? [def.barName] : []), ...lines.map(l => l.name)], top: top - LAYOUT.subLegend + 3, left: LAYOUT.left }));
        if (headerInfo) graphics.push({ id: 'info-' + sub, type: 'text', left: LAYOUT.left, top: top - LAYOUT.subLegend + 2, silent: true, style: { text: '', fontSize: 11, fill: '#d1d4dc' } });
        if (def.hasData && !candles.some(def.hasData)) {
            graphics.push({ type: 'text', left: 'center', top: top + LAYOUT.subHeight / 2 - 6,
                            style: { text: def.noDataText ?? NO_DATA_TEXT, fill: '#5d6270', fontSize: 11 } });
        }
    });
    yAxes.push(...extraYAxes);

    if (headerInfo) {
        // 改在各圖上方顯示當天數值：不用提示框 (只留十字線)，圖例隱藏 (仍保留均線的顯示狀態)，日期只在 K 線上方出現一次
        graphics.push({ id: 'info-main', type: 'text', left: LAYOUT.left, top: maLegend.top, silent: true, style: { text: '', fontSize: 11, fill: '#d1d4dc' } });
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
