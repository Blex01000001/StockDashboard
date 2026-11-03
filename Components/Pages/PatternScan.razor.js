// PatternScan.razor.js：繪製型態掃描的小卡與參數旁的型態範例
// 小卡 = K 線區 (固定高度) + 0 ~ 多個附圖 (成交量 / MACD / KD / RSI / 三大法人 / 外資 / 投信 / 自營商) 依序疊在下方

const UP = '#ef5350';     // 漲：紅 (台股配色)
const DOWN = '#26a69a';   // 跌：綠
const SIGNAL_COLOR = '#ffd54f';   // 型態標示
const HORIZON_COLOR = '#8392A5';  // 觀察日標示
const AXIS_COLOR = '#8392A5';
const LINE_A = '#f5c242';         // 附圖第一條線
const LINE_B = '#42a5f5';         // 附圖第二條線

// 均線：欄位名 (對應 C# PatternCandle 的 camelCase)、圖例名稱、顏色。點圖例可隱藏 / 顯示該條線
// 顏色避開漲紅跌綠，以免和 K 棒混淆
const MA_LINES = [
    { key: 'ma5', name: 'MA5', color: '#f5c242' },
    { key: 'ma10', name: 'MA10', color: '#ab47bc' },
    { key: 'ma20', name: 'MA20', color: '#42a5f5' },
    { key: 'ma60', name: 'MA60', color: '#ff9800' },
    { key: 'ma120', name: 'MA120', color: '#bdbdbd' }
];

const HOLDING_COLOR = '#ab47bc';  // 外資持股比例線

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
        tooltip: c => `${label} ${signed(toLots(c[key]))} 張${extraTooltip ? extraTooltip(c) : ''}`
    };
}

// 附圖定義：bars = 柱狀 (可無)、barName = 柱狀的圖例名稱 (有才列入圖例)、lines = 線 (依指標參數命名；
// ownAxis = 用自己的 Y 軸，例如持股比例 % 與買賣超張數單位不同)、levels = 0~100 擺盪指標的參考線、
// hasData = 判斷該 K 棒有無資料 (整張小卡都沒有時顯示提示)
// settings 為 C# IndicatorSettings (camelCase)
const SUB_CHARTS = {
    volume: {
        bars: c => ({ value: c.volume, itemStyle: { color: c.close >= c.open ? UP : DOWN } }),
        lines: () => [{ key: 'volMa5', name: '量MA5', color: LINE_A }, { key: 'volMa10', name: '量MA10', color: '#ab47bc' }],
        tooltip: c => `量 ${c.volume.toLocaleString()}　量MA5 ${c.volMa5?.toLocaleString() ?? '-'}　量MA10 ${c.volMa10?.toLocaleString() ?? '-'}`
    },
    macd: {
        // OSC 柱狀：正紅負綠
        bars: c => ({ value: c.osc, itemStyle: { color: (c.osc ?? 0) >= 0 ? UP : DOWN } }),
        lines: () => [{ key: 'dif', name: 'DIF', color: LINE_A }, { key: 'macd', name: 'MACD', color: LINE_B }],
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
        [{ key: 'foreignHoldingPct', name: '持股%', color: HOLDING_COLOR, ownAxis: true }],
        c => `　持股 ${c.foreignHoldingPct != null ? c.foreignHoldingPct + '%' : '-'}`),
    trust: netChart('trustNet', '投信'),
    dealer: netChart('dealerNet', '自營商')
};
const NO_DATA_TEXT = '尚無三大法人資料（請到「資料更新」頁同步）';

// 小卡版面 (像素)：K 線區高度固定，每多一個附圖，小卡就增高 SUB_ROW
const LAYOUT = {
    top: 36,          // 上方留給均線圖例與觀察日標籤
    candleHeight: 150,
    subLegend: 18,    // 附圖上方的圖例列
    subHeight: 48,
    bottom: 26,       // 最下方的日期標籤
    left: 48,
    right: 12
};
const SUB_ROW = LAYOUT.subLegend + LAYOUT.subHeight;

export function cardHeight(subCount) {
    return LAYOUT.top + LAYOUT.candleHeight + subCount * SUB_ROW + LAYOUT.bottom;
}

let cardCharts = [];
let previewChart = null;

// 離開頁面時釋放所有圖表
export function disposeAll() {
    cardCharts.forEach(c => c.dispose());
    cardCharts = [];
    if (previewChart) { previewChart.dispose(); previewChart = null; }
}

// 換頁或重新掃描時，先釋放上一批小卡圖表，避免記憶體累積
// visibleMas：預設顯示的均線名稱 (例如 ['MA5','MA20'])；其他均線仍在圖例中 (灰色)，點一下可單張顯示
// horizons：觀察天數 (例如 [5, 10, 20, 60])，在型態成立日之後第 N 根 K 棒畫淡色直線
// subCharts：要疊在 K 線下方的附圖，依序排列，例如 ['volume', 'macd']
// settings：指標參數 (RSI 的圖例名稱會用到)
export function renderPatternCards(cards, visibleMas, horizons, subCharts, settings) {
    cardCharts.forEach(c => c.dispose());
    cardCharts = [];
    const subs = (subCharts || []).filter(s => SUB_CHARTS[s]);
    const selected = Object.fromEntries(MA_LINES.map(m => [m.name, (visibleMas || []).includes(m.name)]));
    for (const card of cards) {
        const dom = document.getElementById(card.containerId);
        if (!dom) continue;
        dom.style.height = cardHeight(subs.length) + 'px';   // 先設定高度再建立圖表
        const chart = echarts.init(dom);
        const option = buildCardOption(card.candles, card.signalIndex, card.barCount, horizons || [], subs, settings || {});
        option.legend[0].selected = selected;
        chart.setOption(option);
        cardCharts.push(chart);
    }
}

// 型態範例：參數改變時重畫 (沿用同一個圖表實例)；固定只畫 K 線 + 成交量
export function renderPreview(containerId, candles, signalIndex, barCount) {
    const dom = document.getElementById(containerId);
    if (!dom) return;
    if (!previewChart || previewChart.getDom() !== dom) {
        if (previewChart) previewChart.dispose();
        previewChart = echarts.init(dom);
    }
    previewChart.setOption(buildPreviewOption(candles, signalIndex, barCount), true);
}

// ── K 線 series (小卡與預覽共用) ──
function candleSeries(candles, signalIndex, barCount, horizons) {
    const dates = candles.map(c => c.date);
    const signalDate = dates[signalIndex];
    const patternMarks = [];
    for (let i = Math.max(0, signalIndex - barCount + 1); i <= signalIndex; i++) {
        patternMarks.push({ coord: [dates[i], candles[i].low] });
    }
    return {
        type: 'candlestick',
        data: candles.map(c => [c.open, c.close, c.low, c.high]),
        itemStyle: { color: UP, color0: DOWN, borderColor: UP, borderColor0: DOWN },
        // 型態成立日：黃色虛線；型態每根 K 棒：下方三角形
        // (markArea 起訖為同一類別時寬度為 0 不會顯示，所以不用它)
        markLine: {
            silent: true, symbol: 'none', label: { show: false },
            lineStyle: { color: SIGNAL_COLOR, type: 'dashed', width: 1 },
            data: [
                { xAxis: signalDate },
                // 觀察日：型態成立後第 N 個交易日 (之後報酬就是算到這一天的收盤)；超出資料範圍的不畫
                ...horizons
                    .filter(h => signalIndex + h < dates.length)
                    .map(h => ({
                        xAxis: dates[signalIndex + h],
                        lineStyle: { color: HORIZON_COLOR, type: 'dotted', width: 1, opacity: 0.55 },
                        label: { show: true, formatter: `${h}日`, position: 'end', color: HORIZON_COLOR, fontSize: 9, distance: 2 }
                    }))
            ]
        },
        markPoint: {
            silent: true, symbol: 'triangle', symbolSize: 8, symbolOffset: [0, 11],
            label: { show: false }, itemStyle: { color: SIGNAL_COLOR },
            data: patternMarks
        }
    };
}

function legendStyle(extra) {
    return {
        itemWidth: 12, itemHeight: 7, itemGap: 8, textStyle: { color: AXIS_COLOR, fontSize: 10 },
        // 隱藏中的線在圖例上調暗 (預設淺灰色會和 MA120 的灰色混淆)
        inactiveColor: '#3a3f4b', inactiveBorderColor: '#3a3f4b',
        ...extra
    };
}

// ── 小卡 ──
function buildCardOption(candles, signalIndex, barCount, horizons, subs, settings) {
    const dates = candles.map(c => c.date);
    const signalDate = dates[signalIndex];
    const axisLine = { lineStyle: { color: AXIS_COLOR } };
    const lastGrid = subs.length;   // 最下方的 grid 才顯示日期

    const grids = [{ left: LAYOUT.left, right: LAYOUT.right, top: LAYOUT.top, height: LAYOUT.candleHeight }];
    const xAxes = [{ type: 'category', data: dates, axisLine, axisTick: { show: false },
                     axisLabel: { show: lastGrid === 0, color: AXIS_COLOR, fontSize: 10, formatter: d => d.slice(5) } }];
    const yAxes = [{ scale: true, axisLine, axisLabel: { color: AXIS_COLOR, fontSize: 10 }, splitLine: { lineStyle: { color: '#2d3139' } } }];
    const legends = [legendStyle({ data: MA_LINES.map(m => m.name), top: 0, right: 8 })];
    const series = [candleSeries(candles, signalIndex, barCount, horizons)];
    const extraYAxes = [];   // 附圖自己的第二個 Y 軸，排在每個 grid 的主 Y 軸之後 (索引才不會錯位)
    const graphics = [];     // 附圖無資料時的提示文字

    for (const m of MA_LINES) {
        series.push({ name: m.name, type: 'line', data: candles.map(c => c[m.key]), showSymbol: false,
                      lineStyle: { width: 1, color: m.color }, itemStyle: { color: m.color } });
    }

    subs.forEach((sub, i) => {
        const def = SUB_CHARTS[sub];
        const g = i + 1;
        const top = LAYOUT.top + LAYOUT.candleHeight + i * SUB_ROW + LAYOUT.subLegend;
        grids.push({ left: LAYOUT.left, right: LAYOUT.right, top, height: LAYOUT.subHeight });
        xAxes.push({ type: 'category', gridIndex: g, data: dates, axisLine, axisTick: { show: false },
                     axisLabel: { show: g === lastGrid, color: AXIS_COLOR, fontSize: 10, formatter: d => d.slice(5) } });
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
        if (def.hasData && !candles.some(def.hasData)) {
            graphics.push({ type: 'text', left: 'center', top: top + LAYOUT.subHeight / 2 - 6,
                            style: { text: NO_DATA_TEXT, fill: '#5d6270', fontSize: 11 } });
        }
    });
    yAxes.push(...extraYAxes);

    return {
        animation: false,
        legend: legends,
        tooltip: {
            trigger: 'axis',
            axisPointer: { type: 'cross' },
            backgroundColor: 'rgba(30, 34, 45, 0.9)',
            borderColor: '#434651',
            textStyle: { color: '#d1d4dc', fontSize: 12 },
            formatter: params => {
                const c = candles[params[0].dataIndex];
                const color = c.close >= c.open ? UP : DOWN;
                return `<div style="font-weight:bold;color:#fff;">${c.date}${c.date === signalDate ? '（型態成立）' : ''}</div>
                    <div style="color:${color}">開 ${c.open}　高 ${c.high}<br/>低 ${c.low}　收 ${c.close}</div>
                    ${subs.map(s => `<div>${SUB_CHARTS[s].tooltip(c, settings)}</div>`).join('')}
                    ${MA_LINES.map(m => `<div style="color:${m.color}">${m.name} ${c[m.key] ?? '-'}</div>`).join('')}`;
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

// ── 型態範例 (預覽) ──
function buildPreviewOption(candles, signalIndex, barCount) {
    const dates = candles.map(c => c.date);
    const axisLine = { lineStyle: { color: AXIS_COLOR } };
    return {
        animation: false,
        grid: [{ left: 6, right: 6, top: 6, height: '68%' }, { left: 6, right: 6, top: '80%', height: '16%' }],
        xAxis: [
            { type: 'category', data: dates, axisLine, axisLabel: { show: false }, axisTick: { show: false } },
            { type: 'category', gridIndex: 1, data: dates, axisLine, axisLabel: { show: false }, axisTick: { show: false } }
        ],
        yAxis: [
            { scale: true, axisLine, axisLabel: { show: false }, splitLine: { show: false } },
            { gridIndex: 1, axisLabel: { show: false }, axisLine: { show: false }, splitLine: { show: false } }
        ],
        series: [
            candleSeries(candles, signalIndex, barCount, []),
            { type: 'bar', xAxisIndex: 1, yAxisIndex: 1, data: candles.map(SUB_CHARTS.volume.bars) }
        ]
    };
}
